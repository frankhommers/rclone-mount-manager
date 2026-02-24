# Persistent Mounts Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make rclone mounts survive app exit and be adopted on next launch, using nohup for process detachment and rclone's RC API for discovery/control.

**Architecture:** Launch rclone via `/bin/sh -c 'nohup rclone ... --rc --rc-no-auth --rc-addr localhost:{port} > {logfile} 2>&1 &'` using CliWrap. On app start, probe each profile's RC port to discover running orphans. Use `core/quit` for graceful shutdown.

**Tech Stack:** .NET 10, CliWrap 3.10, System.Net.Http for RC API calls, CommunityToolkit.Mvvm

---

### Task 1: Add RcPort and EnableRemoteControl to MountProfile

**Files:**
- Modify: `RcloneMountManager.Core/Models/MountProfile.cs`

**Step 1: Add new properties to MountProfile**

Add two new observable properties after the `_backendOptions` field (line 67):

```csharp
[ObservableProperty]
private int _rcPort;

[ObservableProperty]
private bool _enableRemoteControl = true;
```

**Step 2: Build and verify**

Run: `dotnet build RcloneMountManager.slnx`
Expected: Build succeeded, 0 errors

**Step 3: Commit**

```
feat: add RcPort and EnableRemoteControl properties to MountProfile
```

---

### Task 2: Add RC port assignment helper

**Files:**
- Modify: `RcloneMountManager.Core/Services/MountManagerService.cs`
- Create: `RcloneMountManager.Tests/Services/MountManagerServiceRcPortTests.cs`

**Step 1: Write the failing test**

```csharp
using RcloneMountManager.Core.Services;
using Xunit;

namespace RcloneMountManager.Tests.Services;

public class MountManagerServiceRcPortTests
{
    [Fact]
    public void AssignRcPort_ReturnsPortInRange()
    {
        var port = MountManagerService.AssignRcPort("abc123def456");
        Assert.InRange(port, 50000, 59999);
    }

    [Fact]
    public void AssignRcPort_SameIdReturnsSamePort()
    {
        var port1 = MountManagerService.AssignRcPort("abc123def456");
        var port2 = MountManagerService.AssignRcPort("abc123def456");
        Assert.Equal(port1, port2);
    }

    [Fact]
    public void AssignRcPort_DifferentIdsReturnDifferentPorts()
    {
        var port1 = MountManagerService.AssignRcPort("profile1");
        var port2 = MountManagerService.AssignRcPort("profile2");
        Assert.NotEqual(port1, port2);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test RcloneMountManager.slnx --filter "MountManagerServiceRcPortTests"`
Expected: FAIL — `AssignRcPort` does not exist

**Step 3: Write implementation**

Add to `MountManagerService.cs` (before the `RunningMount` record at the bottom):

```csharp
public static int AssignRcPort(string profileId)
{
    var hash = profileId.GetHashCode(StringComparison.OrdinalIgnoreCase);
    return 50000 + (Math.Abs(hash) % 10000);
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test RcloneMountManager.slnx --filter "MountManagerServiceRcPortTests"`
Expected: 3 passed

**Step 5: Commit**

```
feat: add deterministic RC port assignment helper
```

---

### Task 3: Add RcloneRcClient for RC API communication

**Files:**
- Create: `RcloneMountManager.Core/Services/RcloneRcClient.cs`
- Create: `RcloneMountManager.Tests/Services/RcloneRcClientTests.cs`

**Step 1: Write the failing tests**

```csharp
using RcloneMountManager.Core.Services;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace RcloneMountManager.Tests.Services;

public class RcloneRcClientTests
{
    [Fact]
    public async Task GetPidAsync_ReturnsNull_WhenConnectionRefused()
    {
        var client = new RcloneRcClient(new HttpClient());
        var result = await client.GetPidAsync(59999, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task IsAliveAsync_ReturnsFalse_WhenConnectionRefused()
    {
        var client = new RcloneRcClient(new HttpClient());
        var alive = await client.IsAliveAsync(59999, CancellationToken.None);
        Assert.False(alive);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test RcloneMountManager.slnx --filter "RcloneRcClientTests"`
Expected: FAIL — class does not exist

**Step 3: Write implementation**

```csharp
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RcloneMountManager.Core.Services;

public sealed class RcloneRcClient
{
    private readonly HttpClient _httpClient;

    public RcloneRcClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(3);
    }

    public async Task<int?> GetPidAsync(int rcPort, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{rcPort}/core/pid");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("pid", out var pidElement))
            {
                return pidElement.GetInt32();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> IsAliveAsync(int rcPort, CancellationToken cancellationToken)
    {
        var pid = await GetPidAsync(rcPort, cancellationToken);
        return pid.HasValue;
    }

    public async Task<bool> QuitAsync(int rcPort, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{rcPort}/core/quit");
            request.Content = new StringContent("{\"exitCode\":0}", Encoding.UTF8, "application/json");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test RcloneMountManager.slnx --filter "RcloneRcClientTests"`
Expected: 2 passed

**Step 5: Commit**

```
feat: add RcloneRcClient for RC API communication
```

---

### Task 4: Persist RcPort and EnableRemoteControl

**Files:**
- Modify: `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs` — `PersistedProfile`, `LoadProfiles`, `SaveProfiles`

**Step 1: Add fields to PersistedProfile** (around line 2438)

```csharp
public int RcPort { get; set; }
public bool EnableRemoteControl { get; set; } = true;
```

**Step 2: Add to LoadProfiles** (inside the `new MountProfile { ... }` block, after `BackendOptions`)

```csharp
RcPort = saved.RcPort,
EnableRemoteControl = saved.EnableRemoteControl,
```

Also, after constructing the profile, auto-assign RC port if not set:

```csharp
if (profile.RcPort == 0 && !profile.IsRemoteDefinition)
{
    profile.RcPort = MountManagerService.AssignRcPort(profile.Id);
}
```

**Step 3: Add to SaveProfiles** (inside the `new PersistedProfile { ... }` block, after `BackendOptions`)

```csharp
RcPort = profile.RcPort,
EnableRemoteControl = profile.EnableRemoteControl,
```

**Step 4: Also auto-assign RC port when creating new mount profiles**

In the `AddMount` or profile creation logic, after setting the Id:

```csharp
RcPort = MountManagerService.AssignRcPort(id),
EnableRemoteControl = true,
```

Find the profile creation methods (`AddMountAsync` / `AddProfileAsync`) and add the RC port assignment there.

**Step 5: Build and test**

Run: `dotnet build RcloneMountManager.slnx && dotnet test RcloneMountManager.slnx --nologo`
Expected: Build succeeded, all tests pass

**Step 6: Commit**

```
feat: persist RcPort and EnableRemoteControl in profiles
```

---

### Task 5: Replace CliWrap ListenAsync with nohup detached launch

**Files:**
- Modify: `RcloneMountManager.Core/Services/MountManagerService.cs` — `StartRcloneAsync`

**Step 1: Rewrite StartRcloneAsync**

The key changes:
1. Inject `--rc --rc-no-auth --rc-addr localhost:{rcPort}` flags when `EnableRemoteControl` is true
2. Build the full command string for nohup
3. Launch via `/bin/sh -c 'nohup rclone ... > {logfile} 2>&1 &'`
4. Poll RC to confirm startup instead of relying on CliWrap event stream
5. Change `RunningMount` record to hold `int Pid` and `int RcPort` instead of CTS + Task

Replace the `RunningMount` record:

```csharp
private sealed record RunningMount(int Pid, int RcPort);
```

Replace `StartRcloneAsync` (lines 313-393) with:

```csharp
private async Task StartRcloneAsync(MountProfile profile, string mountPoint, Action<string> log, CancellationToken cancellationToken)
{
    if (_runningMounts.ContainsKey(mountPoint))
    {
        throw new InvalidOperationException("rclone mount is already running for this mount point.");
    }

    if (await IsMountedAsync(mountPoint, cancellationToken))
    {
        throw new InvalidOperationException(
            $"Mount point '{mountPoint}' is already in use (possibly from a previous session). Stop the existing mount first.");
    }

    var source = ResolveRcloneSource(profile);
    var binary = string.IsNullOrWhiteSpace(profile.RcloneBinaryPath) ? "rclone" : profile.RcloneBinaryPath;
    var mountCommand = await ResolveRcloneMountCommandAsync(binary, profile.Type, log, cancellationToken);
    var arguments = new List<string> { mountCommand, source, mountPoint };
    await AddQuickConnectArgumentsAsync(profile, arguments, cancellationToken);

    arguments.AddRange(ParseArguments(profile.ExtraOptions));

    foreach (var kvp in profile.MountOptions)
    {
        var flag = "--" + kvp.Key.Replace('_', '-');
        if (string.Equals(kvp.Value, "true", StringComparison.OrdinalIgnoreCase))
        {
            arguments.Add(flag);
        }
        else if (string.Equals(kvp.Value, "false", StringComparison.OrdinalIgnoreCase))
        {
        }
        else if (!string.IsNullOrEmpty(kvp.Value))
        {
            arguments.Add(flag);
            arguments.Add(kvp.Value);
        }
    }

    if (profile.EnableRemoteControl && profile.RcPort > 0)
    {
        if (!arguments.Any(a => a.StartsWith("--rc-addr", StringComparison.OrdinalIgnoreCase)))
        {
            arguments.Add("--rc");
            arguments.Add("--rc-no-auth");
            arguments.Add("--rc-addr");
            arguments.Add($"localhost:{profile.RcPort}");
        }
    }

    var logDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RcloneMountManager", "logs");
    Directory.CreateDirectory(logDir);
    var logFile = Path.Combine(logDir, $"{profile.Id}.log");

    var rcloneArgs = string.Join(" ", arguments.Select(EscapeArgument));
    var shellCommand = $"nohup \"{EscapeForBash(binary)}\" {rcloneArgs} >> \"{EscapeForBash(logFile)}\" 2>&1 &";

    log($"Launching detached: /bin/sh -c '{shellCommand}'");

    var result = await Cli.Wrap("/bin/sh")
        .WithArguments(["-c", shellCommand])
        .WithValidation(CommandResultValidation.None)
        .ExecuteBufferedAsync(cancellationToken);

    if (result.ExitCode != 0)
    {
        throw new InvalidOperationException($"Failed to launch rclone: {result.StandardError}");
    }

    if (profile.EnableRemoteControl && profile.RcPort > 0)
    {
        var rcClient = new RcloneRcClient(new HttpClient());
        int? pid = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await Task.Delay(500, cancellationToken);
            pid = await rcClient.GetPidAsync(profile.RcPort, cancellationToken);
            if (pid.HasValue)
            {
                break;
            }
        }

        if (pid.HasValue)
        {
            _runningMounts.TryAdd(mountPoint, new RunningMount(pid.Value, profile.RcPort));
            log($"rclone {mountCommand} started (PID {pid.Value}, RC port {profile.RcPort}).");
        }
        else
        {
            log($"WARN: rclone launched but RC not responding on port {profile.RcPort}.");
        }
    }
    else
    {
        await Task.Delay(2000, cancellationToken);
        if (await IsMountedAsync(mountPoint, cancellationToken))
        {
            log($"rclone {mountCommand} started (no RC, mount point verified).");
            _runningMounts.TryAdd(mountPoint, new RunningMount(0, 0));
        }
        else
        {
            throw new InvalidOperationException("Mount did not appear after launch. Check the log file for errors.");
        }
    }
}
```

**Step 2: Update StopAsync** (lines 65-90) to use RC quit:

```csharp
public async Task StopAsync(MountProfile profile, Action<string> log, CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(profile);

    var mountPoint = ResolveMountPoint(profile.MountPoint);

    if (IsRcloneMountType(profile.Type) && _runningMounts.TryRemove(mountPoint, out var runningMount))
    {
        if (runningMount.RcPort > 0)
        {
            log("Sending quit via RC...");
            var rcClient = new RcloneRcClient(new HttpClient());
            await rcClient.QuitAsync(runningMount.RcPort, cancellationToken);

            for (var attempt = 0; attempt < 10; attempt++)
            {
                await Task.Delay(500, cancellationToken);
                if (!await IsMountedAsync(mountPoint, cancellationToken))
                {
                    log("rclone stopped via RC.");
                    return;
                }
            }

            log("RC quit timed out, falling back to umount.");
        }
        else
        {
            log("No RC port, falling back to umount.");
        }
    }
    else if (IsRcloneMountType(profile.Type))
    {
        if (profile.EnableRemoteControl && profile.RcPort > 0)
        {
            log("No tracked process; trying RC quit for orphan...");
            var rcClient = new RcloneRcClient(new HttpClient());
            if (await rcClient.QuitAsync(profile.RcPort, cancellationToken))
            {
                for (var attempt = 0; attempt < 10; attempt++)
                {
                    await Task.Delay(500, cancellationToken);
                    if (!await IsMountedAsync(mountPoint, cancellationToken))
                    {
                        log("Orphan rclone stopped via RC.");
                        return;
                    }
                }
            }
        }

        log("No tracked rclone process found; attempting unmount of orphan mount.");
    }

    await UnmountAsync(mountPoint, log, cancellationToken);
}
```

**Step 3: Update IsRunning to also check RC**

Add a method to check if a mount is adoptable:

```csharp
public async Task<int?> ProbeRcPidAsync(int rcPort, CancellationToken cancellationToken)
{
    if (rcPort <= 0) return null;
    var rcClient = new RcloneRcClient(new HttpClient());
    return await rcClient.GetPidAsync(rcPort, cancellationToken);
}
```

**Step 4: Build and test**

Run: `dotnet build RcloneMountManager.slnx && dotnet test RcloneMountManager.slnx --nologo`
Expected: Build succeeded, all existing tests pass (some may need updating for the new `RunningMount` signature)

**Step 5: Commit**

```
feat: launch rclone via nohup for persistent mounts, stop via RC quit
```

---

### Task 6: Adopt orphan mounts on app startup

**Files:**
- Modify: `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs` — add `AdoptOrphanMountsAsync` method, call it from `RunRuntimeMonitoringLoopAsync`

**Step 1: Add adoption method**

Add a new method near `VerifyStartupProfilesAsync`:

```csharp
private async Task AdoptOrphanMountsAsync(CancellationToken cancellationToken)
{
    var mountProfiles = Profiles
        .Where(p => !p.IsRemoteDefinition && p.EnableRemoteControl && p.RcPort > 0)
        .ToList();

    if (mountProfiles.Count == 0) return;

    AppendLog(ProfileLogCategory.Startup, ProfileLogStage.Initialization,
        $"Probing {mountProfiles.Count} mount profiles for running orphans...");

    foreach (var profile in mountProfiles)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pid = await _mountService.ProbeRcPidAsync(profile.RcPort, cancellationToken);
        var mountPoint = ResolveMountPoint(profile.MountPoint);
        var isMounted = await _mountService.IsMountedAsync(mountPoint, cancellationToken);

        if (pid.HasValue && isMounted)
        {
            _mountService.AdoptMount(mountPoint, pid.Value, profile.RcPort);
            AppendLog(profile.Id, ProfileLogCategory.Startup, ProfileLogStage.Verification,
                $"Adopted running mount (PID {pid.Value}, RC port {profile.RcPort}).");
        }
        else if (pid.HasValue && !isMounted)
        {
            AppendLog(profile.Id, ProfileLogCategory.Startup, ProfileLogStage.Verification,
                $"Stale rclone on port {profile.RcPort} (PID {pid.Value}), sending quit.");
            await _mountService.StopViaRcAsync(profile.RcPort, cancellationToken);
        }
        else if (!pid.HasValue && isMounted)
        {
            AppendLog(profile.Id, ProfileLogCategory.Startup, ProfileLogStage.Verification,
                "WARN: Mount point is active but no RC connection. Unmanaged external mount.");
        }
    }
}
```

**Step 2: Add AdoptMount and StopViaRcAsync to MountManagerService**

```csharp
public void AdoptMount(string mountPoint, int pid, int rcPort)
{
    _runningMounts.TryAdd(mountPoint, new RunningMount(pid, rcPort));
}

public async Task StopViaRcAsync(int rcPort, CancellationToken cancellationToken)
{
    var rcClient = new RcloneRcClient(new HttpClient());
    await rcClient.QuitAsync(rcPort, cancellationToken);
}
```

**Step 3: Call from RunRuntimeMonitoringLoopAsync**

Insert before `VerifyStartupProfilesAsync`:

```csharp
private async Task RunRuntimeMonitoringLoopAsync(CancellationToken cancellationToken)
{
    try
    {
        await AdoptOrphanMountsAsync(cancellationToken);
        await VerifyStartupProfilesAsync(cancellationToken);

        while (await _runtimeRefreshWaiter(RuntimeRefreshCadence, cancellationToken))
        {
            await RefreshAllRuntimeStatesAsync(cancellationToken);
        }
    }
    // ... existing catch blocks
}
```

**Step 4: Wire up _mountService**

The ViewModel needs a reference to `MountManagerService` for `ProbeRcPidAsync`, `AdoptMount`, and `StopViaRcAsync`. Check the existing constructor for how `_mountStartRunner` and `_mountStopRunner` are wired and follow the same pattern — or pass the service directly.

**Step 5: Build and test**

Run: `dotnet build RcloneMountManager.slnx && dotnet test RcloneMountManager.slnx --nologo`
Expected: Build succeeded, all tests pass

**Step 6: Commit**

```
feat: adopt orphan rclone mounts on app startup via RC probe
```

---

### Task 7: Update MountHealthService to use RC for IsRunning check

**Files:**
- Modify: `RcloneMountManager.Core/Services/MountHealthService.cs`

**Step 1: Add RC-based running probe**

The current `_isRunningProbe` checks the in-memory `_runningMounts` dictionary. After adoption, this works. But we can also enhance `VerifyAsync` to check RC as an additional signal.

Update the `isRunning` check in `VerifyAsync` to also try RC if available:

This is optional and can be deferred. The adoption in Task 6 ensures `_runningMounts` is populated on startup, so the existing probe works.

**Step 2: Commit if changes were made**

---

### Task 8: Update GenerateScript to include RC flags

**Files:**
- Modify: `RcloneMountManager.Core/Services/MountManagerService.cs` — `GenerateScript` method

**Step 1: Add RC flags to generated script**

After the mount options loop (around line 291), before `builder.AppendLine()`:

```csharp
if (profile.EnableRemoteControl && profile.RcPort > 0)
{
    var hasRcAddr = profile.MountOptions.ContainsKey("rc_addr") ||
                    profile.ExtraOptions.Contains("--rc-addr", StringComparison.OrdinalIgnoreCase);
    if (!hasRcAddr)
    {
        builder.Append(" --rc --rc-no-auth --rc-addr ");
        builder.Append(EscapeArgument($"localhost:{profile.RcPort}"));
    }
}
```

**Step 2: Build and test**

Run: `dotnet build RcloneMountManager.slnx && dotnet test RcloneMountManager.slnx --nologo`

**Step 3: Commit**

```
feat: include RC flags in generated mount scripts
```

---

### Task 9: Migrate existing profiles that have manual --rc in MountOptions

**Files:**
- Modify: `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs` — `LoadProfiles`

**Step 1: Add migration logic**

After constructing each profile in `LoadProfiles`, if the profile has `rc` or `rc_addr` in `MountOptions`, extract the port and remove those keys:

```csharp
if (!profile.IsRemoteDefinition && profile.MountOptions.TryGetValue("rc_addr", out var rcAddr))
{
    if (rcAddr.Contains(':'))
    {
        var portStr = rcAddr.Split(':').Last();
        if (int.TryParse(portStr, out var port) && port > 0)
        {
            profile.RcPort = port;
        }
    }

    profile.MountOptions.Remove("rc");
    profile.MountOptions.Remove("rc_addr");
    profile.MountOptions.Remove("rc_no_auth");
}
```

**Step 2: Build and test**

**Step 3: Commit**

```
feat: migrate manual RC options from MountOptions to RcPort on load
```

---

### Task 10: Remove stale mount-point-already-in-use error for adopted mounts

**Files:**
- Modify: `RcloneMountManager.Core/Services/MountManagerService.cs` — `StartRcloneAsync`

**Step 1: Update the IsMountedAsync check**

The current code (line 320-324) throws if the mount point is already in use. After implementing adoption, we should check if it's adopted (in `_runningMounts`) before throwing. If it's already adopted and running, just return without error:

```csharp
if (await IsMountedAsync(mountPoint, cancellationToken))
{
    if (_runningMounts.ContainsKey(mountPoint))
    {
        log("Mount point is already active and tracked.");
        return;
    }

    throw new InvalidOperationException(
        $"Mount point '{mountPoint}' is already in use (possibly from a previous session). Stop the existing mount first.");
}
```

**Step 2: Build and test**

**Step 3: Commit**

```
fix: allow start on already-adopted mounts without error
```

---

### Task 11: End-to-end manual test

**Step 1: Build and run the app**

Run: `dotnet build RcloneMountManager.slnx`

**Step 2: Manual test checklist**

1. Open app, select Passwords mount profile
2. Verify RcPort has been auto-assigned (check in profile JSON)
3. Click "Start mount" — verify mount appears and RC port is logged
4. Close the app
5. Verify rclone is still running: `curl -s -X POST http://localhost:{port}/core/pid`
6. Verify mount is still active: `mount | grep Passwords`
7. Reopen the app
8. Verify the Passwords profile shows as mounted/healthy (adopted)
9. Click "Stop mount" — verify graceful RC shutdown
10. Verify rclone process is gone and mount point is unmounted

**Step 3: Final commit if any fixes needed**
