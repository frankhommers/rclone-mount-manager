---
phase: 03-startup-diagnostics-and-log-isolation
plan: 03
subsystem: ui
tags: [diagnostics, timeline, avalonia, viewmodel, xunit, startup]

# Dependency graph
requires:
  - phase: 03-02
    provides: explicit diagnostics filter state and deterministic typed timeline projection
provides:
  - in-app diagnostics panel controls for profile scope and startup-only timeline isolation
  - typed diagnostics timeline rows with timestamp/severity/stage/message fields bound directly to UI
  - regression assertions for timestamp rendering, startup filtering, and profile-switch leak prevention
affects: [phase-closure, diagnostics-troubleshooting, startup-failure-investigation]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - typed diagnostics row projection record at ViewModel edge for UI bindings
    - explicit diagnostics empty-state messaging when filters produce no rows
    - UI-facing diagnostics tests assert bound row fields instead of raw ad-hoc string fragments

key-files:
  created:
    - .planning/phases/03-startup-diagnostics-and-log-isolation/03-03-SUMMARY.md
  modified:
    - RcloneMountManager.GUI/Views/MainWindow.axaml
    - RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs
    - RcloneMountManager.Tests/ViewModels/MainWindowViewModelDiagnosticsTests.cs

key-decisions:
  - "Bind diagnostics panel controls directly to dedicated diagnostics filter state in MainWindowViewModel so users can isolate startup evidence without changing edit context."
  - "Project typed diagnostics rows (timestamp/severity/stage/message) for UI consumption while retaining a legacy display string list for compatibility and deterministic tests."
  - "Show an explicit 'No diagnostics for current filter.' empty-state message to avoid ambiguous blank timeline panels."

patterns-established:
  - "Diagnostics UI binding: filter controls -> typed timeline row projection -> ListBox columns + empty-state border."
  - "Diagnostics regression style: assert bound row properties (ProfileId/TimestampText/StageText) to validate UI-facing behavior at ViewModel level."

# Metrics
duration: 2m 20s
completed: 2026-02-21
---

# Phase 3 Plan 3: Startup Diagnostics Panel and Timeline UX Summary

**Main window diagnostics now provides profile/startup isolation controls and timestamped typed timeline rows so startup failures can be traced by time, severity, stage, and message in-app.**

## Performance

- **Duration:** 2m 20s
- **Started:** 2026-02-21T21:53:33Z
- **Completed:** 2026-02-21T21:55:53Z
- **Tasks:** 3
- **Files modified:** 3

## Accomplishments
- Replaced the former Activity area with a diagnostics timeline panel that includes profile scope selection and a startup-path-only toggle.
- Added typed diagnostics row projection in the ViewModel and bound the UI to timestamp/severity/stage/message fields plus an explicit empty-state message.
- Extended diagnostics tests to assert timestamp formatting, startup-only row filtering, and no event leakage when profile selection changes.

## Task Commits

Each task was committed atomically:

1. **Task 1: Replace activity list with diagnostics timeline panel controls** - `31636b2` (feat)
2. **Task 2: Bind timestamped diagnostics rows to typed projection output** - `7c7dcd1` (feat)
3. **Task 3: Add diagnostics UI-facing regression assertions** - `d000ee7` (test)

## Files Created/Modified
- `RcloneMountManager.GUI/Views/MainWindow.axaml` - Added diagnostics filter controls, typed row ListBox layout columns, and empty-state panel text.
- `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs` - Added typed `DiagnosticsTimelineRow` projection, UI-facing diagnostics collections/properties, and row formatting at projection edge.
- `RcloneMountManager.Tests/ViewModels/MainWindowViewModelDiagnosticsTests.cs` - Added assertions for timestamp formatting, startup-only stage filtering, and profile-switch isolation behavior.

## Decisions Made
- Keep diagnostics controls visible in the main diagnostics panel so startup timeline isolation is a first-class in-screen workflow.
- Preserve legacy `Logs` display projection while introducing typed diagnostics rows, minimizing compatibility risk for existing bindings/tests.
- Treat empty diagnostics output as explicit UI state with instructional text instead of a blank list.

## Deviations from Plan

None - plan executed exactly as written.

## Authentication Gates

None.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Phase 3 diagnostics UX delivery is complete with typed timeline evidence and regression coverage for user-facing filtering behavior.
- No blockers identified; roadmap execution is ready to advance beyond phase 3.

---
*Phase: 03-startup-diagnostics-and-log-isolation*
*Completed: 2026-02-21*
