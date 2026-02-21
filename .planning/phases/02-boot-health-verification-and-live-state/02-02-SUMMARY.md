---
phase: 02-boot-health-verification-and-live-state
plan: 02
subsystem: ui
tags: [avalonia, runtime-state, lifecycle, health, viewmodel, xunit]

# Dependency graph
requires:
  - phase: 01-startup-enablement-and-safety-gates
    provides: startup toggle/preflight orchestration and profile persistence baseline
  - phase: 02-boot-health-verification-and-live-state
    provides: typed lifecycle and health models with MountHealthService verification
provides:
  - mount start/stop/refresh commands now publish explicit lifecycle transitions
  - main window surfaces lifecycle and health state for selected and listed profiles
  - deterministic ViewModel tests lock runtime transition behavior for OBS-01
affects: [phase-02-plan-03, phase-03-startup-diagnostics-and-log-isolation, ui-runtime-observability]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - command lifecycle hooks set typed runtime state before and after mount operations
    - status text rendered from typed runtime state mapping instead of boolean/status string concatenation

key-files:
  created:
    - RcloneMountManager.Tests/ViewModels/MainWindowViewModelRuntimeStateTests.cs
  modified:
    - RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs
    - RcloneMountManager.GUI/Views/MainWindow.axaml

key-decisions:
  - "Set Mounting before start and preserve Failed when start/stop commands throw."
  - "Set Idle after successful stop when mount presence probe confirms unmounted."
  - "Display lifecycle and health directly in UI bindings for both profile list rows and selected profile details."

patterns-established:
  - "Transition truth pattern: command handlers publish typed lifecycle state transitions before invoking external commands."
  - "Presentation pattern: UI reads lifecycle/health from runtime state properties, not ad-hoc LastStatus text generation."

# Metrics
duration: 3 min
completed: 2026-02-21
---

# Phase 2 Plan 2: Runtime Lifecycle and Health UI Wiring Summary

**Main window mount actions now emit explicit idle/mounting/mounted/failed transitions while showing per-profile lifecycle and health verdicts directly from typed runtime state.**

## Performance

- **Duration:** 3 min
- **Started:** 2026-02-21T20:17:00Z
- **Completed:** 2026-02-21T20:20:33Z
- **Tasks:** 3
- **Files modified:** 3

## Accomplishments

- Added typed lifecycle transition orchestration in `MainWindowViewModel` for start/stop/refresh flows, including explicit `Mounting`, post-action verification, and successful-stop `Idle` mapping.
- Replaced status-string truth with runtime-state mapping so `StatusText` and compatibility fields derive from typed lifecycle/health values.
- Updated main UI to show lifecycle and health in profile rows and selected-profile runtime panel while preserving startup preflight panel layout.
- Added deterministic `MainWindowViewModelRuntimeStateTests` covering mount success/failure transitions, stop-to-idle behavior, degraded/failed health mapping, and status-text output.

## Task Commits

Each task was committed atomically:

1. **Task 1: Add lifecycle transition hooks around mount commands** - `c73beff` (feat)
2. **Task 2: Surface lifecycle and health state in main UI** - `fc92403` (feat)
3. **Task 3: Add runtime state transition ViewModel tests** - `d63dcd6` (test)

## Files Created/Modified

- `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs` - lifecycle transition hooks, runtime-state status mapping, and selected-profile lifecycle/health text binding properties.
- `RcloneMountManager.GUI/Views/MainWindow.axaml` - runtime lifecycle/health display in profile list and selected profile section.
- `RcloneMountManager.Tests/ViewModels/MainWindowViewModelRuntimeStateTests.cs` - regression coverage for success/failure transitions and health verdict projection.

## Decisions Made

- Keep `RunBusyActionAsync` as the operation envelope but set typed runtime transition states inside command handlers to preserve deterministic lifecycle semantics.
- Treat successful stop as `Idle/Unknown` once mount presence probe reports unmounted, avoiding false failed states after intentional unmounts.
- Keep user-facing status readable by formatting lifecycle/health text from runtime state while retaining compatibility fields (`IsMounted`, `IsRunning`, `LastStatus`).

## Deviations from Plan

None - plan executed exactly as written.

## Authentication Gates

None.

## Issues Encountered

- Initial parallel final verification (`dotnet build` and `dotnet test` together) caused a transient PDB file lock; reran tests sequentially and all checks passed.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Runtime lifecycle and health states are now visible and test-covered in main flow.
- Ready for `02-03-PLAN.md` startup fan-out verification and periodic refresh loop work.

---
*Phase: 02-boot-health-verification-and-live-state*
*Completed: 2026-02-21*
