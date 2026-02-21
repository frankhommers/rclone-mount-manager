# Phase 3: Startup Diagnostics and Log Isolation - Research

**Researched:** 2026-02-21
**Domain:** In-app startup diagnostics timeline and per-profile log isolation in Avalonia/.NET MVVM
**Confidence:** HIGH

## Summary

Phase 3 should formalize logging as typed lifecycle evidence, not display strings. The current implementation already timestamps entries and stores logs per profile in `_profileLogs`, but it stores only formatted text (`"[HH:mm:ss] ..."`) and routes many async command logs through `AppendLog(string)` that depends on `SelectedProfile` at log time. That makes robust filtering, sorting, and timeline reconstruction fragile.

The standard approach for this stack is to keep a structured event model in memory (timestamp, profile ID, lifecycle stage, severity, message, optional error details) and project that model into UI views (selected profile log, filtered timeline, startup-only timeline). Keep Serilog as the durable sink for full diagnostics while the UI uses a bounded in-memory ring per profile for fast interaction.

For OBS-02/OBS-03, use a single event pipeline in `MainWindowViewModel` fed by mount/startup/runtime monitor actions, then filter by profile via deterministic predicates (profile ID + optional startup session window). Do not parse display strings to recover state.

**Primary recommendation:** Add a typed `ProfileLogEvent` pipeline keyed by `profile.Id`, always log with explicit profile context, and bind filtered views from that typed store (profile filter + startup timeline filter) while continuing to emit the same events to Serilog file logs.

## Standard Stack

The established libraries/tools for this domain:

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET `DateTimeOffset` + standard `"O"` formatting | net10.0 | Timestamp events with offset-safe round-trip format | Official .NET guidance for invariant round-trip date/time representation |
| `ObservableCollection<T>` | net10.0 | UI-bound event list updates | Native collection-change notifications for MVVM views |
| `Dictionary<TKey,TValue>` + `StringComparer.OrdinalIgnoreCase` | net10.0 | Per-profile indexed event storage | O(1)-ish keyed retrieval and stable case-insensitive profile ID lookups |
| Serilog + Serilog.Sinks.File | 4.2.0 / 6.0.0 | Durable diagnostics output and retention/rolling | Existing app logger, supports rolling policies and output templates |
| LINQ (`Where`) | net10.0 | Deterministic event filtering by profile/timeline criteria | Standard deferred-execution filtering over in-memory sequences |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Avalonia `ListBox`/`ItemsControl` binding | 11.3.12 | Lightweight timeline rendering | Use when showing one profile timeline and modest row counts |
| Avalonia `DataGrid` (`Avalonia.Controls.DataGrid`) | 11.3.12 | Columned diagnostics table (time/severity/stage/message) | Use if phase plan includes sortable columns and richer table UX |
| xUnit | 2.9.3 | Deterministic ViewModel filtering and routing tests | Use for profile-routing, timestamp ordering, and startup timeline tests |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Typed `ProfileLogEvent` objects in memory | Keep only formatted strings in `List<string>` | String-only logs block safe filtering/sorting and force brittle parsing |
| Existing ListBox UI | Add `DataGrid` package + table UI | Better filtering/sorting UX, but introduces extra package + theme wiring |
| In-memory per-profile ring + Serilog files | Build a custom local DB event store now | Overkill for OBS-02/03; adds migration/retention complexity too early |

**Installation:**
```bash
# No new package required for minimum Phase 3 scope.
# Optional richer table UX:
dotnet add RcloneMountManager.GUI package Avalonia.Controls.DataGrid --version 11.3.12
```

## Architecture Patterns

### Recommended Project Structure
```
RcloneMountManager.Core/
└── Models/
    └── ProfileLogEvent.cs                # typed event record (profile, ts, stage, severity, message)

RcloneMountManager.GUI/
├── ViewModels/
│   └── MainWindowViewModel.cs            # log ingest + filter projections + startup timeline selection
└── Views/
    └── MainWindow.axaml                  # profile log filter controls + timeline list/table

RcloneMountManager.Tests/
└── ViewModels/
    └── MainWindowViewModelDiagnosticsTests.cs  # routing/filter/timeline deterministic tests
```

### Pattern 1: Structured Log Event Pipeline
**What:** Write one typed event for each lifecycle/preflight/startup/runtime observation.
**When to use:** All mount/startup/monitoring paths.
**Example:**
```csharp
// Source: repo pattern + .NET DateTimeOffset docs
public sealed record ProfileLogEvent(
    string ProfileId,
    DateTimeOffset Timestamp,
    string Category,
    string Severity,
    string Message,
    string? Error = null);
```

### Pattern 2: Explicit Profile Routing (Never Implicit Selected Profile)
**What:** Every async callback logs with explicit `profileId` captured at command start.
**When to use:** `StartMount`, `StopMount`, startup verification, monitoring loop callbacks.
**Example:**
```csharp
// Source: current MainWindowViewModel callback model, hardened routing
var profileId = profile.Id;
await _mountStartRunner(profile, line => AppendLog(profileId, line), cancellationToken);
```

### Pattern 3: Projection-Based Filtering
**What:** Keep canonical event store, project filtered views for selected profile and startup-only timeline.
**When to use:** UI profile switch, filter toggle change, new event appended.
**Example:**
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/api/system.linq.enumerable.where?view=net-10.0
var filtered = events
    .Where(e => e.ProfileId == selectedProfileId)
    .Where(e => !startupOnly || e.Category == "startup");
```

### Pattern 4: Keep Display Formatting at View Edge
**What:** Store raw `DateTimeOffset`; format to text only for rendering/export.
**When to use:** Binding templates and copy/export actions.
**Example:**
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/standard/base-types/standard-date-and-time-format-strings
var text = $"[{evt.Timestamp:O}] {evt.Severity}: {evt.Message}";
```

### Anti-Patterns to Avoid
- **Implicit selected-profile logging:** async callbacks can be misattributed if user switches profile mid-operation.
- **String parsing as data model:** parsing `"[HH:mm:ss] ..."` to infer profile/stage is brittle and locale-sensitive.
- **Unbounded in-memory logs:** causes memory growth and sluggish UI; keep bounded per profile.
- **UI-only logs with no durable sink linkage:** makes reboot/startup failures hard to investigate later.

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Log file rolling/retention | Custom file rotation and prune logic | `Serilog.Sinks.File` rolling + retention options | Already implemented and battle-tested |
| Event filtering engine | Custom query/parser DSL for profile filters | LINQ `Where` predicates over typed events | Clear, testable, no parser edge cases |
| Timestamp serialization format | Ad-hoc local-time string format as canonical storage | `DateTimeOffset` + standard `"O"` round-trip format | Preserves offset/time correctly across boundaries |
| Grid control behavior | Homegrown virtualized table | Avalonia `DataGrid` when tabular UX is needed | Existing control supports table interactions |

**Key insight:** Most Phase 3 risk is attribution and modeling, not rendering. Use built-in collection/filter/time primitives and keep custom logic focused on event semantics.

## Common Pitfalls

### Pitfall 1: Wrong Profile Attribution During Async Work
**What goes wrong:** Logs from one profile appear under another profile after user selection changes.
**Why it happens:** callback uses `AppendLog(string)` that resolves profile via current `SelectedProfile`.
**How to avoid:** capture `profile.Id` once and pass `AppendLog(profileId, line)` in all async runners.
**Warning signs:** timeline entries mention profile A action while viewing profile B session.

### Pitfall 2: Non-Round-Trip Timestamp Model
**What goes wrong:** timeline ordering and cross-session comparisons are ambiguous.
**Why it happens:** only human-formatted local timestamps are stored (`HH:mm:ss`) with no offset/date.
**How to avoid:** store `DateTimeOffset` on event; format only in UI/export.
**Warning signs:** same-time entries across midnight or timezone changes sort incorrectly.

### Pitfall 3: Startup Path Not Isolated
**What goes wrong:** startup failure evidence is mixed with later manual actions.
**Why it happens:** no startup-session/event category boundary.
**How to avoid:** tag events with `Category`/`Stage` (startup, runtime-refresh, manual-start, manual-stop).
**Warning signs:** user cannot answer "what failed during startup" without manually scanning all activity.

### Pitfall 4: String-Only Event Store
**What goes wrong:** adding severity/stage/profile filters requires brittle regex/string matching.
**Why it happens:** canonical model is `List<string>` instead of typed event record.
**How to avoid:** typed event object as source of truth; derive display strings secondarily.
**Warning signs:** filter logic starts depending on prefixes like `ERR:` and textual fragments.

### Pitfall 5: UI Jank with Large Log Lists
**What goes wrong:** profile switching or filter toggling freezes briefly.
**Why it happens:** full list rebuilds with unbounded entries.
**How to avoid:** bounded ring size per profile plus incremental projection updates.
**Warning signs:** noticeable lag after long runtime sessions.

## Code Examples

Verified patterns from official sources:

### Timestamp with Round-Trip Format
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/standard/base-types/standard-date-and-time-format-strings
var now = DateTimeOffset.UtcNow;
var serialized = now.ToString("O");
```

### Observable Collection for UI Updates
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/api/system.collections.objectmodel.observablecollection-1?view=net-10.0
public ObservableCollection<ProfileLogEvent> VisibleEvents { get; } = new();
```

### LINQ Predicate Filter
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/api/system.linq.enumerable.where?view=net-10.0
var startupFailures = allEvents
    .Where(e => e.ProfileId == profileId)
    .Where(e => e.Category == "startup")
    .Where(e => e.Severity == "error");
```

### Serilog File Sink Rolling and Retention
```csharp
// Source: https://github.com/serilog/serilog-sinks-file
Log.Logger = new LoggerConfiguration()
    .WriteTo.File("log.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
    .CreateLogger();
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Plain string activity entries | Structured events + projected text views | Current phase target (2026) | Enables robust profile/timeline filters |
| Implicit profile routing from selected UI state | Explicit profile-id routing at log source | Current phase target (2026) | Removes async misattribution risk |
| Minimal list UI only | Optional table UX with `DataGrid` package | Avalonia docs updated 2026-01-07 | Better diagnostics ergonomics if needed |

**Deprecated/outdated:**
- Treating display text as canonical diagnostic data.
- Inferring severity solely from message prefixes without typed severity field.

## Open Questions

1. **Phase 3 persistence depth for in-app timeline**
   - What we know: OBS-02/03 require viewing timestamped logs and filtering by profile.
   - What's unclear: whether in-app timeline must survive app restart (beyond Serilog files).
   - Recommendation: keep session-scoped in-app timeline for Phase 3; rely on Serilog file logs for historical persistence.

2. **Startup timeline boundary semantics**
   - What we know: users must isolate startup failure path quickly.
   - What's unclear: boundary should be "app launch window" vs. "start-at-login profiles verification pass".
   - Recommendation: define startup scope as events emitted during `VerifyStartupProfilesAsync` and startup monitoring initialization.

3. **UI control choice (ListBox vs DataGrid)**
   - What we know: current UI already uses ListBox and can satisfy minimal requirements.
   - What's unclear: whether sortable columns are desired in phase scope.
   - Recommendation: plan with ListBox baseline; add DataGrid only if planner wants sorting/column-level filtering in this phase.

## Sources

### Primary (HIGH confidence)
- Repository source inspection:
  - `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`
  - `RcloneMountManager.GUI/Views/MainWindow.axaml`
  - `RcloneMountManager.Core/Models/ProfileRuntimeState.cs`
  - `RcloneMountManager.GUI/Program.cs`
- https://learn.microsoft.com/en-us/dotnet/standard/base-types/standard-date-and-time-format-strings - `"O"` round-trip format guidance
- https://learn.microsoft.com/en-us/dotnet/api/system.collections.objectmodel.observablecollection-1?view=net-10.0 - UI collection change semantics
- https://learn.microsoft.com/en-us/dotnet/api/system.linq.enumerable.where?view=net-10.0 - deterministic filtering semantics
- https://learn.microsoft.com/en-us/dotnet/api/system.datetimeoffset?view=net-10.0 - offset-aware time model
- https://docs.avaloniaui.net/docs/reference/controls/datagrid/ - DataGrid package/style requirements (updated 2026-01-07)
- https://api-docs.avaloniaui.net/docs/T_Avalonia_Controls_DataGrid - DataGrid API assembly/surface
- https://github.com/serilog/serilog-sinks-file - rolling/retention/output template behavior

### Secondary (MEDIUM confidence)
- https://github.com/serilog/serilog/wiki/Writing-Log-Events - message-template and correlation conventions
- https://rclone.org/flags/ - logging flags (`--log-file`, `--log-format`, `--use-json-log`) for subprocess diagnostics context

### Tertiary (LOW confidence)
- None.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - based on official .NET/Avalonia/Serilog docs and existing repo dependencies.
- Architecture: HIGH - directly derived from current code paths and verified framework capabilities.
- Pitfalls: HIGH - grounded in observed current implementation behavior and official time/filter collection semantics.

**Research date:** 2026-02-21
**Valid until:** 2026-03-23 (30 days)
