# Phase 2: Boot Health Verification and Live State - Research

**Researched:** 2026-02-21
**Domain:** Post-login mount health verification and live lifecycle observability in a macOS-first Avalonia/.NET app
**Confidence:** HIGH

## Summary

Phase 2 should add a first-class runtime state model (lifecycle + health) and a deterministic post-login verification pipeline for auto-mount profiles. The current code exposes booleans (`IsMounted`, `IsRunning`) and a free-form status string, but there is no boot-time health pass and no continuous state transition surface. This means users can see stale/optimistic status after login.

The standard approach for this stack is: (1) keep startup registration truth (`launchctl bootstrap/bootout` path from Phase 1), (2) compute runtime truth via explicit probes after app launch and on interval, and (3) map probe outcomes into user-facing states (`idle`, `mounting`, `mounted`, `failed`) plus health (`healthy`, `degraded`, `failed`). Use bounded checks with timeout so a hung mount path does not freeze UI state updates.

For macOS + rclone, avoid inventing custom introspection protocols. Use existing primitives already in this repo: `mount` detection, tracked rclone process state, and short filesystem usability probes. Optionally enrich with rclone RC liveness (`core/pid` / `core/stats`) only when RC is enabled and reachable.

**Primary recommendation:** Introduce a `MountHealthService` + `ProfileRuntimeState` model and drive a periodic live-state loop in `MainWindowViewModel` that runs boot verification for all `StartAtLogin=true` profiles, classifies health (`healthy/degraded/failed`), and emits lifecycle transitions (`idle/mounting/mounted/failed`) on every mount action and periodic probe tick.

## Standard Stack

The established libraries/tools for this domain:

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET runtime (`Task.WaitAsync`, `PeriodicTimer`) | net10.0 | Bounded async probes + recurring status checks | Native timeout/tick primitives; no extra scheduler dependency |
| `CliWrap` | 3.10.0 | Execute `mount`, `rclone rc`, and related commands with explicit exit handling | Already used throughout service layer for command orchestration |
| Avalonia threading (`Dispatcher.UIThread`, `DispatcherTimer`) | 11.3.12 | UI-safe propagation of live status updates | Native Avalonia mechanism for UI-thread state changes |
| macOS `launchctl` domain model (`gui/<uid>`) | OS built-in | Startup domain targeting context for post-login verification scope | Existing Phase 1 startup architecture depends on this |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `Serilog` | 4.2.0 | Structured runtime state transition logs | Record lifecycle/health transitions for future diagnostics phase |
| rclone RC API (`core/pid`, `core/stats`, `rc/noop`) | rclone docs updated 2025-11-21 | Optional liveness telemetry from running mount process | Use for rclone profiles when RC endpoint is configured/reachable |
| `System.IO` directory enumeration | net10.0 | Mount usability probe (can enumerate/open mount path) | Verify mount is usable, not just present |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `PeriodicTimer` polling loop in service/viewmodel | `DispatcherTimer` only | `DispatcherTimer` is UI-thread native but easier to overload UI if probes are heavy; prefer background probe + UI marshal |
| mount + filesystem probe truth | parsing `launchctl print` for service health | `launchctl print` output is explicitly non-API; brittle to parse |
| explicit lifecycle enum + health enum | keep booleans/string status only | booleans cannot represent degraded state or transitions cleanly |

**Installation:**
```bash
# No new packages are required for Phase 2.
# Use existing net10.0 + Avalonia 11.3.12 + CliWrap 3.10.0 stack.
```

## Architecture Patterns

### Recommended Project Structure
```
RcloneMountManager.Core/
├── Models/
│   ├── ProfileRuntimeState.cs        # lifecycle + health + timestamps + last error
│   ├── MountLifecycleState.cs        # Idle, Mounting, Mounted, Failed
│   └── MountHealthState.cs           # Unknown, Healthy, Degraded, Failed
└── Services/
    └── MountHealthService.cs         # boot verification + periodic usability checks

RcloneMountManager.GUI/
└── ViewModels/
    └── MainWindowViewModel.cs        # orchestrates transitions and binds runtime state
```

### Relevant Existing Code Locations
- `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs:606` - current status refresh is selected-profile only and string-based.
- `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs:484` - mount lifecycle commands (`StartMountAsync`, `StopMountAsync`, startup toggle) where transition hooks should be added.
- `RcloneMountManager.Core/Services/MountManagerService.cs:88` - current mount presence check (`IsMountedAsync`) used as one signal in health classification.
- `RcloneMountManager.Core/Models/MountProfile.cs:58` - current booleans/status fields (`IsMounted`, `IsRunning`, `LastStatus`) to evolve into explicit runtime model.
- `RcloneMountManager.GUI/App.axaml.cs:26` - main viewmodel construction point where boot health verification startup should be triggered.

### Pattern 1: Dual State Model (Lifecycle vs Health)
**What:** Track operation lifecycle separately from health verdict.
**When to use:** Always; lifecycle alone cannot represent degraded runtime.
**Example:**
```csharp
// Source: repo requirements HEAL-01/HEAL-02/OBS-01
public enum MountLifecycleState { Idle, Mounting, Mounted, Failed }
public enum MountHealthState { Unknown, Healthy, Degraded, Failed }

public sealed record ProfileRuntimeState(
    MountLifecycleState Lifecycle,
    MountHealthState Health,
    DateTimeOffset LastCheckedAt,
    string? LastError);
```

### Pattern 2: Boot Verification Fan-Out for Auto-Mount Profiles
**What:** On app startup, verify all `StartAtLogin` profiles and compute truth before presenting final status.
**When to use:** App initialization and profile load completion.
**Example:**
```csharp
// Source: existing startup profile persistence path in MainWindowViewModel
foreach (var profile in Profiles.Where(p => p.StartAtLogin))
{
    var result = await _mountHealthService.VerifyAsync(profile, cancellationToken);
    ApplyRuntimeState(profile, result);
}
```

### Pattern 3: Bounded Usability Probe + Presence Probe
**What:** Combine mount presence (`mount`) and a short filesystem usability probe with timeout.
**When to use:** Boot verification and periodic checks.
**Example:**
```csharp
// Source: .NET Task.WaitAsync docs + existing MountManagerService.IsMountedAsync pattern
var mounted = await _mountManagerService.IsMountedAsync(profile.MountPoint, cancellationToken);
var usable = await Task.Run(() => Directory.EnumerateFileSystemEntries(mountPath).Any(), cancellationToken)
    .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

var health = !mounted
    ? MountHealthState.Failed
    : usable
        ? MountHealthState.Healthy
        : MountHealthState.Degraded;
```

### Pattern 4: Polling Loop with Explicit Cancellation and UI Marshaling
**What:** Run periodic probes off UI thread, marshal updates onto UI thread.
**When to use:** Live state updates during startup and normal operation.
**Example:**
```csharp
// Source: .NET PeriodicTimer docs + Avalonia Dispatcher usage pattern
using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
while (await timer.WaitForNextTickAsync(cancellationToken))
{
    var snapshot = await _mountHealthService.VerifyAllAsync(Profiles, cancellationToken);
    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ApplySnapshot(snapshot));
}
```

### Anti-Patterns to Avoid
- **Single status string as source of truth:** cannot encode transition semantics required by OBS-01.
- **Blocking UI thread for filesystem probes:** stale UI and apparent hangs on slow/unresponsive mounts.
- **Treating `mounted=true` as automatically healthy:** violates HEAL-01 when mount is present but unusable.
- **Parsing `launchctl print` output for automation:** explicitly non-stable output per manpage.

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Periodic scheduler | Custom thread + sleep loop with manual drift handling | `PeriodicTimer` (or `DispatcherTimer` for UI-only ticks) | Built-in cancellation semantics and cleaner lifecycle |
| Timeout wrapper logic | Ad-hoc race between `Task.Delay` and operation | `Task.WaitAsync(TimeSpan, CancellationToken)` | Standardized timeout behavior + clear exceptions |
| Startup domain inference | Parse PIDs/sessions manually | Existing `gui/<uid>` launchctl targeting model | Already validated in Phase 1 and deterministic |
| rclone process introspection protocol | Custom sidecar IPC | rclone RC endpoints (`core/pid`, `core/stats`, `rc/noop`) | Official API surface with documented commands |
| Health check state storage | Free-form status string parsing | Typed enums + immutable/record state objects | Deterministic transitions and testability |

**Key insight:** Phase 2 is mostly an orchestration/truth-model problem; reliability comes from combining existing probes and modeling state transitions explicitly, not from inventing new transport or parser layers.

## Common Pitfalls

### Pitfall 1: Presence-Only Health
**What goes wrong:** profile shows mounted/healthy while mount path is unusable.
**Why it happens:** only `mount` command output is checked.
**How to avoid:** require both presence and bounded usability probe.
**Warning signs:** users can see mount in status but file operations fail/hang.

### Pitfall 2: UI Thread Probe Execution
**What goes wrong:** UI freezes during startup or periodic checks.
**Why it happens:** synchronous filesystem/network checks run on dispatcher thread.
**How to avoid:** run probes in background and marshal final state updates onto UI thread.
**Warning signs:** delayed button response while status refresh runs.

### Pitfall 3: Unbounded Probe Duration
**What goes wrong:** status loop stalls indefinitely on a hung mount.
**Why it happens:** no timeout/cancellation on usability checks.
**How to avoid:** use `WaitAsync` timeout and classify timeout as degraded/failed.
**Warning signs:** state timestamp stops advancing for affected profile.

### Pitfall 4: Non-API Output Parsing
**What goes wrong:** breakage after OS update.
**Why it happens:** parser depends on unstable `launchctl print` text.
**How to avoid:** use exit codes and stable command semantics; avoid print parsing for logic.
**Warning signs:** fragile regex/string logic around launchctl output text.

### Pitfall 5: Collapsing Lifecycle and Health into One Enum
**What goes wrong:** cannot represent "mounted but degraded" or startup transient states.
**Why it happens:** one-axis model forced to do two jobs.
**How to avoid:** keep lifecycle (`idle/mounting/mounted/failed`) and health (`healthy/degraded/failed`) separate.
**Warning signs:** repeated conditionals trying to infer degraded from failed/mounted flags.

## Code Examples

Verified patterns from official sources:

### Run Bounded Probe with Timeout
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.waitasync?view=net-10.0
var usable = await Task
    .Run(() => Directory.EnumerateFileSystemEntries(mountPath).Any(), cancellationToken)
    .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
```

### Periodic Background Verification Loop
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/api/system.threading.periodictimer?view=net-10.0
using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
while (await timer.WaitForNextTickAsync(cancellationToken))
{
    await VerifyAndPublishAsync(cancellationToken);
}
```

### Query rclone RC for Liveness (Optional)
```bash
# Source: https://rclone.org/commands/rclone_rc/
rclone rc --url "http://localhost:5572/" rc/noop
rclone rc --url "http://localhost:5572/" core/pid
```

### Use launchctl Domain Targeting (Reference)
```bash
# Source: local `man launchctl`
launchctl bootstrap "gui/$UID" "$HOME/Library/LaunchAgents/com.rclonemountmanager.profile.<id>.plist"
launchctl bootout "gui/$UID/com.rclonemountmanager.profile.<id>"
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Free-form status string (`LastStatus`) | Typed lifecycle + health model | Current phase target (2026) | Enables deterministic UI state and verification logic |
| Manual refresh on selected profile only | Periodic fan-out verification across startup-enabled profiles | Current phase target (2026) | Delivers truthful post-login state without user action |
| Unbounded status checks | Timeout-bounded probes (`WaitAsync`) | .NET modern API (net6+) and active in net10 | Prevents hung checks from freezing status pipeline |

**Deprecated/outdated:**
- Parsing `launchctl print` output for automation logic: explicitly unsupported as API (`man launchctl`).
- Boolean-only runtime truth (`IsMounted`/`IsRunning`) as final UX state for HEAL/OBS requirements.

## Planning Implications

- Add one plan focused on runtime model refactor (`MountProfile`/viewmodel binding) before UI polish.
- Add one plan for health service implementation (presence + usability + timeout + classification).
- Add one plan for live loop orchestration and transition hooks in all command paths (`start`, `stop`, startup verify, refresh).
- Add test tasks for both state transitions and health classification edge cases (timeout, mounted-but-unusable, startup-enabled profile failures).
- Keep diagnostics depth limited in this phase: log transition events now, defer rich log filtering to Phase 3.

## Open Questions

1. **Exact degraded vs failed thresholding**
   - What we know: requirements need explicit degraded and failed visibility.
   - What's unclear: whether timeout on usability should always be degraded or failed.
   - Recommendation: treat mounted + unusable as degraded; not mounted as failed.

2. **RC endpoint usage policy**
   - What we know: default profile options include `rc=true` and `rc_addr=localhost:<port>`.
   - What's unclear: whether all user-created profiles preserve RC options and whether auth is configured.
   - Recommendation: make RC checks optional enrichment; never make health truth depend solely on RC availability.

3. **Probe cadence for startup window**
   - What we know: startup transitions need to be observable and truthful.
   - What's unclear: best polling interval for responsiveness vs load.
   - Recommendation: use 2-5s cadence initially, then tune in Phase 4 policy work if needed.

## Sources

### Primary (HIGH confidence)
- Local `man launchctl` - domain specifiers (`gui/<uid>`), `bootstrap/bootout`, `enable/disable`, and explicit warning that `print` output is not API.
- Local `man launchd.plist` - behavior of `Label`, `ProgramArguments`, `RunAtLoad`, `KeepAlive`, `Disabled`.
- https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.waitasync?view=net-10.0 - timeout and cancellation semantics for bounded probes.
- https://learn.microsoft.com/en-us/dotnet/api/system.threading.periodictimer?view=net-10.0 - single-consumer periodic async tick pattern.
- https://learn.microsoft.com/en-us/dotnet/api/system.io.directory.enumeratefilesystementries?view=net-10.0 - filesystem entry enumeration behavior for usability checks.
- https://api-docs.avaloniaui.net/docs/T_Avalonia_Threading_DispatcherTimer - UI-thread timer behavior in Avalonia 11.3.12.
- https://rclone.org/commands/rclone_mount/ - mount behavior, daemon-wait caveat on macOS/BSD, VFS cache implications.
- https://rclone.org/commands/rclone_rc/ - RC command usage (`rc/noop`, `core/pid`) and connection parameters.
- https://rclone.org/rc/ - RC API endpoints (`core/stats`, `mount/listmounts`, auth requirements by endpoint).

### Secondary (MEDIUM confidence)
- Repository code inspection:
  - `RcloneMountManager.Core/Services/MountManagerService.cs`
  - `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`
  - `RcloneMountManager.Core/Models/MountProfile.cs`

### Tertiary (LOW confidence)
- None.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - based on official .NET, Avalonia, rclone docs and current repo dependency versions.
- Architecture: HIGH - directly aligned to existing code structure and verified platform/service semantics.
- Pitfalls: HIGH - grounded in official docs (non-API warnings, timer/probe behavior) plus concrete repo constraints.

**Research date:** 2026-02-21
**Valid until:** 2026-03-23 (30 days)
