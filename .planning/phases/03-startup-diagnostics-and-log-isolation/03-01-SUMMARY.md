---
phase: 03-startup-diagnostics-and-log-isolation
plan: 01
subsystem: ui
tags: [diagnostics, observability, avalonia, viewmodel, xunit]

# Dependency graph
requires:
  - phase: 02-03
    provides: startup/runtime monitoring seams and runtime-state verification flow in MainWindowViewModel
provides:
  - canonical typed profile diagnostics events in core models
  - explicit profile-attributed async logging pipeline in MainWindowViewModel
  - deterministic regression tests for attribution, startup-category events, and bounded retention
affects: [03-02-startup-timeline-ui, 03-03-observability-hardening, diagnostics]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - typed ProfileLogEvent source of truth with category/stage/severity
    - explicit profile-id callback routing for async operations
    - bounded per-profile diagnostics retention with display projection at view-model edge

key-files:
  created:
    - RcloneMountManager.Core/Models/ProfileLogEvent.cs
    - RcloneMountManager.Tests/ViewModels/MainWindowViewModelDiagnosticsTests.cs
  modified:
    - RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs

key-decisions:
  - "Model lifecycle diagnostics as typed events with enum-based category/stage/severity instead of string-only entries."
  - "Capture profile.Id at async operation start and route callback logs through explicit profile-aware append helpers."
  - "Keep in-memory diagnostics bounded to 250 entries per profile while projecting typed events to display strings only for UI."

patterns-established:
  - "Diagnostics pipeline: create ProfileLogEvent -> store per profile -> trim to cap -> project to Logs for selected profile."
  - "Startup timeline isolation: emit explicit startup category events for monitor initialization and verification steps."

# Metrics
duration: 4m 8s
completed: 2026-02-21
---

# Phase 3 Plan 1: Startup Diagnostics Typed Event Foundation Summary

**Startup and lifecycle evidence now flows through typed, profile-keyed diagnostics events with explicit async attribution and bounded in-memory retention.**

## Performance

- **Duration:** 4m 8s
- **Started:** 2026-02-21T21:41:11Z
- **Completed:** 2026-02-21T21:45:19Z
- **Tasks:** 3
- **Files modified:** 3

## Accomplishments
- Added a canonical `ProfileLogEvent` model and enums for category/stage/severity so diagnostics are queryable beyond formatted text.
- Refactored `MainWindowViewModel` logging internals to typed per-profile event buffers with explicit profile routing in async mount/startup callbacks.
- Added deterministic diagnostics tests covering selected-profile drift safety, per-profile bounded retention, and startup-category verification events.

## Task Commits

Each task was committed atomically:

1. **Task 1: Add canonical typed profile diagnostics event model** - `d0c64c0` (feat)
2. **Task 2: Refactor ViewModel logging to typed per-profile event storage** - `6798056` (feat)
3. **Task 3: Add diagnostics pipeline tests for attribution and bounded retention** - `0b0cdab` (test)

## Files Created/Modified
- `RcloneMountManager.Core/Models/ProfileLogEvent.cs` - Typed diagnostics record and enums for lifecycle/startup event semantics.
- `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs` - Typed event ingestion, explicit profile routing, startup-category emission, and bounded log projection.
- `RcloneMountManager.Tests/ViewModels/MainWindowViewModelDiagnosticsTests.cs` - Regression coverage for async attribution, bounded storage, and startup verification categorization.

## Decisions Made
- Use enum-backed `ProfileLogCategory`, `ProfileLogStage`, and `ProfileLogSeverity` to preserve timeline semantics without string parsing.
- Route async operation logging through captured `profile.Id` callbacks to avoid `SelectedProfile` drift during long-running operations.
- Preserve existing UI `Logs` behavior by projecting typed events to display strings at the ViewModel edge instead of storing display text as source of truth.

## Deviations from Plan

None - plan executed exactly as written.

## Authentication Gates

None.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Typed diagnostics foundations are in place for startup/runtime timeline filtering and richer observability UX in subsequent phase 3 plans.
- No blockers identified for `03-02-PLAN.md`.

---
*Phase: 03-startup-diagnostics-and-log-isolation*
*Completed: 2026-02-21*
