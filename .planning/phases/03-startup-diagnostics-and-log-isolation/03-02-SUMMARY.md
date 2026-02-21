---
phase: 03-startup-diagnostics-and-log-isolation
plan: 02
subsystem: ui
tags: [diagnostics, timeline, filters, avalonia, viewmodel, xunit]

# Dependency graph
requires:
  - phase: 03-01
    provides: typed profile diagnostics event pipeline with bounded retention
provides:
  - explicit diagnostics filter state decoupled from SelectedProfile editing context
  - deterministic timeline projection over typed events with startup-only scope
  - regression tests for profile filter correctness, startup isolation, and deterministic recomputation
affects: [03-03-observability-hardening, diagnostics, startup-debugging]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - explicit diagnostics filter state modeled separately from profile edit selection
    - deterministic timeline projection via typed-event filtering and stable ordering
    - test-driven verification of filter recomputation and startup-only category isolation

key-files:
  created: []
  modified:
    - RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs
    - RcloneMountManager.Tests/ViewModels/MainWindowViewModelDiagnosticsTests.cs

key-decisions:
  - "Introduce dedicated diagnostics filter properties (`SelectedDiagnosticsProfileId`, `StartupTimelineOnly`) rather than reusing `SelectedProfile`."
  - "Rebuild visible diagnostics rows from typed events on each filter/event change to keep projection deterministic."
  - "Treat startup timeline scope as category-based (`ProfileLogCategory.Startup`) to include startup monitor initialization and verification while excluding manual/runtime refresh noise."

patterns-established:
  - "Diagnostics projection: typed store -> filter by profile and scope -> stable sort -> display row formatting."
  - "Filter stability: diagnostics filter selection remains explicit and only falls back when selected id is invalid."

# Metrics
duration: 3m 54s
completed: 2026-02-21
---

# Phase 3 Plan 2: Startup Diagnostics Filter and Timeline Projection Summary

**Diagnostics now projects a deterministic, profile-isolated startup timeline with explicit filter state and regression coverage for recomputation correctness.**

## Performance

- **Duration:** 3m 54s
- **Started:** 2026-02-21T21:47:53Z
- **Completed:** 2026-02-21T21:51:47Z
- **Tasks:** 3
- **Files modified:** 2

## Accomplishments
- Added dedicated diagnostics filter state to the ViewModel (`SelectedDiagnosticsProfileId`, `StartupTimelineOnly`) with profile-selector options derived from `Profiles`.
- Implemented deterministic timeline projection over typed events with stable sorting and startup-only category filtering.
- Added deterministic diagnostics tests validating profile isolation, startup-only timeline behavior, chronological ordering, and recomputation stability when filters change.

## Task Commits

Each task was committed atomically:

1. **Task 1: Add diagnostics filter state for profile and startup timeline scope** - `df903b1` (feat)
2. **Task 2: Project filtered typed events into timeline rows** - `2d99e9d` (feat)
3. **Task 3: Extend diagnostics tests for filter correctness and startup isolation** - `2f8a21e` (test)

## Files Created/Modified
- `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs` - Added explicit diagnostics filter properties, profile filter options, deterministic typed-event projection, startup-only scope filtering, and profile id projection in timeline rows.
- `RcloneMountManager.Tests/ViewModels/MainWindowViewModelDiagnosticsTests.cs` - Added deterministic tests for profile filtering, startup-only isolation, timeline ordering, and filter recomputation.

## Decisions Made
- Keep diagnostics filtering explicitly independent from `SelectedProfile` editing context to prevent selection drift from corrupting analysis context.
- Recompute visible diagnostics timeline from the typed event store on any relevant input change (events, filters, profile list, selection fallback) rather than mutating the displayed list incrementally.
- Define startup timeline scope by `ProfileLogCategory.Startup` so startup monitor initialization and verification remain visible while non-startup operational chatter is excluded.

## Deviations from Plan

None - plan executed exactly as written.

## Authentication Gates

None.

## Issues Encountered

- Initial isolated worktree setup could not see untracked `.planning` plan artifacts; execution continued in the primary working tree where planning artifacts existed.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Diagnostics filter/projection behavior is now deterministic and covered by targeted tests, providing a stable base for phase 3 hardening.
- No blockers identified for `03-03-PLAN.md`.

---
*Phase: 03-startup-diagnostics-and-log-isolation*
*Completed: 2026-02-21*
