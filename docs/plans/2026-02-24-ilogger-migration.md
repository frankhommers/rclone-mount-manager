# IHost + ILogger Migration Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Introduce `Microsoft.Extensions.Hosting` with DI container, replace all `Action<string> log` callbacks and `AppendLog` with proper `ILogger<T>`, move runtime monitoring to `BackgroundService`. Single Serilog pipeline: terminal + file + DiagnosticsSink → UI.

**Architecture:** `IHost` in Program.cs manages DI, logging, lifetime. All services registered as singletons. Serilog integrated via `UseSerilog()`. Services receive `ILogger<T>` via constructor injection — no more `Action<string> log` callbacks. `MainWindowViewModel` receives services via DI. Runtime monitoring becomes a `BackgroundService`. `DiagnosticsSink` → `OnSerilogEvent` is the single path to UI diagnostics. `AppendLog` deleted entirely.

**Tech Stack:** Microsoft.Extensions.Hosting, Serilog.Extensions.Hosting, existing DiagnosticsSink, Avalonia

---

## Phase 1: Foundation — IHost + packages

### Task 1: Add NuGet packages

**Files:**
- Modify: `RcloneMountManager.Core/RcloneMountManager.Core.csproj`
- Modify: `RcloneMountManager.GUI/RcloneMountManager.GUI.csproj`
- Modify: `RcloneMountManager.Tests/RcloneMountManager.Tests.csproj`

**Step 1:** Add packages

```bash
dotnet add RcloneMountManager.Core package Microsoft.Extensions.Logging.Abstractions
dotnet add RcloneMountManager.GUI package Microsoft.Extensions.Hosting
dotnet add RcloneMountManager.GUI package Serilog.Extensions.Hosting
dotnet add RcloneMountManager.Tests package Microsoft.Extensions.Logging.Abstractions
```

Note: `Microsoft.Extensions.Hosting` brings in `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Configuration` transitively. `Serilog.Extensions.Hosting` provides `UseSerilog()` extension.

**Step 2:** Verify build: `dotnet build`

**Step 3: Commit:** `git add -A && git commit -m "chore: add Microsoft.Extensions.Hosting and Serilog.Extensions.Hosting packages"`

---

### Task 2: Set up IHost in Program.cs

**Files:**
- Modify: `RcloneMountManager.GUI/Program.cs`

**Step 1:** Restructure Program.cs to create host, configure Serilog, register services, then start Avalonia:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

// Configure Serilog first (before host, so it captures startup errors)
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(logPath, ...)
    .WriteTo.Sink(DiagnosticsSink.Instance)
    .CreateLogger();

var host = Host.CreateDefaultBuilder(args)
    .UseSerilog()  // Routes M.E.Logging → Serilog
    .ConfigureServices(services =>
    {
        services.AddSingleton<MountManagerService>();
        services.AddSingleton<LaunchAgentService>();
        services.AddSingleton<RcloneBackendService>();
        services.AddSingleton<MountHealthService>();
        services.AddSingleton<StartupPreflightService>();
        services.AddSingleton<MainWindowViewModel>();
    })
    .Build();

// Store host globally for Avalonia to access
App.Services = host.Services;

// Start host (starts BackgroundServices) then run Avalonia
await host.StartAsync();
BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
await host.StopAsync();
```

Keep the existing single-instance Mutex and named pipe logic.

**Step 2:** Update `App.axaml.cs` to resolve ViewModel from DI:
```csharp
public static IServiceProvider Services { get; set; } = null!;
```

In `OnFrameworkInitializationCompleted`:
```csharp
var viewModel = Services.GetRequiredService<MainWindowViewModel>();
```

**Step 3:** Build: `dotnet build`

**Step 4: Commit:** `git add -A && git commit -m "feat: set up IHost with DI and Serilog integration"`

---

## Phase 2: Migrate services to ILogger

### Task 3: Add ILogger to MountManagerService

**Files:**
- Modify: `RcloneMountManager.Core/Services/MountManagerService.cs`

The service has 8 methods with `Action<string> log`. Replace with `ILogger<MountManagerService>`.

**Step 1:** Add constructor:
```csharp
private readonly ILogger<MountManagerService> _logger;

public MountManagerService(ILogger<MountManagerService> logger)
{
    _logger = logger;
}
```

No optional/null parameter — DI always provides it. Tests use `NullLogger<T>.Instance`.

**Step 2:** Remove `Action<string> log` from all 4 public methods: `StartAsync`, `StopAsync`, `TestConnectionAsync`, `TestBackendConnectionAsync`.

**Step 3:** Remove `Action<string> log` from all 4 private methods: `StartRcloneAsync`, `StartNfsAsync`, `UnmountAsync`, `ResolveRcloneMountCommandAsync`.

**Step 4:** Replace every `log(...)` with `_logger.LogInformation(...)`. For profile-specific logs, use `BeginScope`:
```csharp
using (_logger.BeginScope(new Dictionary<string, object>
{
    ["ProfileId"] = profile.Id,
    ["ProfileName"] = profile.Name ?? profile.Id,
}))
{
    // method body with _logger.LogInformation/Warning/Error calls
}
```

For `ClassifyRcloneStderrLine` output, use appropriate log level based on severity.

**Step 5:** Build (tests will fail — expected): `dotnet build`

**Step 6: Commit:** `git add -A && git commit -m "refactor: replace Action<string> log with ILogger in MountManagerService"`

---

### Task 4: Add ILogger to LaunchAgentService

**Files:**
- Modify: `RcloneMountManager.Core/Services/LaunchAgentService.cs`

**Step 1:** Add `ILogger<LaunchAgentService>` to constructor (keep existing optional params for command runner, uid provider, etc.):
```csharp
private readonly ILogger<LaunchAgentService> _logger;

public LaunchAgentService(
    ILogger<LaunchAgentService> logger,
    string? appDataDirectory = null,
    ...)
{
    _logger = logger;
    // ... rest unchanged
}
```

**Step 2:** Remove `Action<string> log` from `EnableAsync` and `DisableAsync`. Replace `log(...)` with `_logger.LogInformation(...)`.

**Step 3:** Build: `dotnet build`

**Step 4: Commit:** `git add -A && git commit -m "refactor: replace Action<string> log with ILogger in LaunchAgentService"`

---

### Task 5: Add ILogger to remaining services

**Files:**
- Modify: `RcloneMountManager.Core/Services/RcloneBackendService.cs`
- Modify: `RcloneMountManager.Core/Services/MountHealthService.cs`
- Modify: `RcloneMountManager.Core/Services/StartupPreflightService.cs`

**Step 1:** Add `ILogger<T>` constructor parameter to each. These services may not currently log, but having the logger enables future logging and makes DI registration consistent.

**Step 2:** Build: `dotnet build`

**Step 3: Commit:** `git add -A && git commit -m "refactor: add ILogger to RcloneBackendService, MountHealthService, StartupPreflightService"`

---

## Phase 3: Update ViewModel

### Task 6: Refactor MainWindowViewModel for DI

**Files:**
- Modify: `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`

This is the biggest change. The ViewModel currently creates services itself and has 39 `AppendLog` call sites.

**Step 1:** Change constructor to accept services via DI:
```csharp
public MainWindowViewModel(
    ILogger<MainWindowViewModel> logger,
    MountManagerService mountManagerService,
    LaunchAgentService launchAgentService,
    MountHealthService mountHealthService,
    RcloneBackendService rcloneBackendService,
    StartupPreflightService startupPreflightService)
```

Remove internal service creation. Services are now injected.

**Step 2:** Add `ILogger<MainWindowViewModel>` field. Create `ProfileScope` helper:
```csharp
private IDisposable ProfileScope(string profileId, ProfileLogCategory category, ProfileLogStage stage)
{
    var profileName = Profiles.FirstOrDefault(p =>
        string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase))?.Name ?? profileId;
    return _logger.BeginScope(new Dictionary<string, object>
    {
        ["ProfileId"] = profileId,
        ["ProfileName"] = profileName,
        ["LogCategory"] = category.ToString(),
        ["LogStage"] = stage.ToString(),
    });
}
```

**Step 3:** Delete all 3 `AppendLog` methods.

**Step 4:** Replace every `AppendLog(...)` call with `ProfileScope` + `_logger.Log*`:
```csharp
// Before:
AppendLog(profileId, ProfileLogCategory.ManualStart, ProfileLogStage.Initialization,
    $"Starting mount '{profile.Name}'...");

// After:
using (ProfileScope(profileId, ProfileLogCategory.ManualStart, ProfileLogStage.Initialization))
    _logger.LogInformation("Starting mount...");
```

For the overload without profileId (uses SelectedProfile), resolve profileId inline:
```csharp
// Before:
AppendLog(ProfileLogCategory.General, ProfileLogStage.Completion, "Saved profile changes.");

// After:
if (SelectedProfile is { } sp)
    using (ProfileScope(sp.Id, ProfileLogCategory.General, ProfileLogStage.Completion))
        _logger.LogInformation("Saved profile changes.");
```

**Step 5:** Remove `Action<string> log` callback lambdas from `_mountStartRunner`, `_mountStopRunner`, `_startupEnableRunner`, `_startupDisableRunner`. Update delegate types:
```csharp
// Before: Func<MountProfile, Action<string>, CancellationToken, Task>
// After:  Func<MountProfile, CancellationToken, Task>
```

Similarly for startup runner delegates that also pass `string scriptContent`.

Keep `Task.Run` wrappers for `_mountStartRunner`, `_mountStopRunner`, `_startupPreflightRunner` — they're for UI responsiveness (NFS I/O), not logging.

**Step 6:** Update `OnSerilogEvent` — verify it extracts `LogCategory`, `LogStage`, `ProfileId`, `ProfileName` from `BeginScope` properties (Serilog enriches from `LogContext` which `BeginScope` pushes to). The `ExtractEnumProperty` helper and `DiagnosticsSink.ExtractProfileId` should already work since `BeginScope` with Serilog's `UseSerilog()` uses `LogContext` under the hood.

Verify `RenderMessage` produces clean messages (no `[ProfileName]` prefix baked in).

**Step 7:** Build: `dotnet build`

**Step 8: Commit:** `git add -A && git commit -m "refactor: replace AppendLog with ILogger in MainWindowViewModel, accept services via DI"`

---

### Task 7: Move runtime monitoring to BackgroundService

**Files:**
- Create: `RcloneMountManager.GUI/Services/RuntimeMonitoringService.cs`
- Modify: `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`
- Modify: `RcloneMountManager.GUI/Program.cs`

**Step 1:** Create `RuntimeMonitoringService` as `BackgroundService`:
```csharp
public sealed class RuntimeMonitoringService : BackgroundService
{
    private readonly ILogger<RuntimeMonitoringService> _logger;
    private readonly MainWindowViewModel _viewModel;

    public RuntimeMonitoringService(
        ILogger<RuntimeMonitoringService> logger,
        MainWindowViewModel viewModel)
    {
        _logger = logger;
        _viewModel = viewModel;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Move RunRuntimeMonitoringLoopAsync logic here
        // Use _logger instead of AppendLog
    }
}
```

**Step 2:** Register in DI: `services.AddHostedService<RuntimeMonitoringService>();`

**Step 3:** Remove `InitializeRuntimeMonitoring`, `StopRuntimeMonitoring`, `_runtimeMonitoringActive`, `_runtimeMonitoringGate`, `_runtimeMonitoringCts` from ViewModel. The host manages the lifecycle.

**Step 4:** The `AdoptOrphanMountsAsync`, `VerifyStartupProfilesAsync`, runtime verification loop stay as methods on the ViewModel but are called by the BackgroundService.

**Step 5:** Build: `dotnet build`

**Step 6: Commit:** `git add -A && git commit -m "refactor: move runtime monitoring to BackgroundService"`

---

## Phase 4: Fix tests

### Task 8: Update all service tests

**Files:**
- Modify: `RcloneMountManager.Tests/Services/MountManagerServiceTests.cs`
- Modify: `RcloneMountManager.Tests/Services/LaunchAgentServiceTests.cs`
- Modify: `RcloneMountManager.Tests/Services/MountHealthServiceTests.cs`
- Modify: `RcloneMountManager.Tests/Services/StartupPreflightServiceTests.cs`
- Modify: any other service tests

**Step 1:** Update service construction to pass `NullLogger<T>.Instance`.

**Step 2:** Remove `Action<string> log` and `_ => {}` from all call sites.

**Step 3:** Where tests captured log lines for assertions, either:
- Remove the assertion (if it just verified a log message)
- Use a test `ILogger` that captures messages if the assertion is important

**Step 4:** Run tests: `dotnet test`

**Step 5: Commit:** `git add -A && git commit -m "test: update service tests for ILogger constructors"`

---

### Task 9: Update ViewModel tests

**Files:**
- Modify: `RcloneMountManager.Tests/ViewModels/MainWindowViewModelDiagnosticsTests.cs`
- Modify: `RcloneMountManager.Tests/ViewModels/MainWindowViewModelRuntimeStateTests.cs`
- Modify: `RcloneMountManager.Tests/ViewModels/MainWindowViewModelSidebarSelectionTests.cs`
- Modify: any other ViewModel test files
- Modify: `RcloneMountManager.Tests/ModuleInit.cs`

**Step 1:** Update `CreateViewModel` helpers — constructor signature changed (DI-style with services + logger). For tests, create services with `NullLogger<T>.Instance` and pass them.

**Step 2:** Update delegate types (no more `Action<string> log` parameter).

**Step 3:** `ModuleInit.cs` already configures Serilog with `DiagnosticsSink`. Verify `Enrich.FromLogContext()` is present (needed for `BeginScope` properties).

**Step 4:** Run all tests: `dotnet test`

**Step 5: Commit:** `git add -A && git commit -m "test: update ViewModel tests for DI and ILogger migration"`

---

## Phase 5: Cleanup

### Task 10: Remove dead code

**Files:**
- Modify: `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`

**Step 1:** Remove `ResolveSeverity` if unused (OnSerilogEvent uses Serilog `LogEventLevel`).

**Step 2:** Remove any remaining `FromAppendLog` references.

**Step 3:** Review `_profileLogs` lock — still needed for concurrent access from Serilog sink thread vs UI thread. Keep it.

**Step 4:** Remove test-only optional constructor parameters from ViewModel if DI handles everything.

**Step 5:** Run all tests: `dotnet test`

**Step 6: Commit:** `git add -A && git commit -m "refactor: remove dead code after IHost migration"`

---

### Task 11: Final verification

**Step 1:** Run `dotnet run` in GUI directory.
**Step 2:** Verify terminal output matches diagnostics tab.
**Step 3:** Test: start mount, stop mount, toggle startup, test connection — all show in diagnostics.
**Step 4:** Verify zero `AppendLog` references: `grep -rn "AppendLog" RcloneMountManager.GUI/ RcloneMountManager.Core/ RcloneMountManager.Tests/`
**Step 5:** Verify zero `Action<string> log` in service signatures: `grep -rn "Action<string> log" RcloneMountManager.Core/`
