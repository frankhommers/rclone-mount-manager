---
phase: 01-startup-enablement-and-safety-gates
plan: 03
subsystem: ui
tags: [startup, preflight, launchagent, avalonia, viewmodel, testing]

# Dependency graph
requires:
  - phase: 01-startup-enablement-and-safety-gates
    provides: startup preflight report models and launch-agent command semantics used by the startup toggle flow
provides:
  - Main window now exposes a user-invokable startup preflight command with explicit check reporting.
  - Startup enable now runs preflight gates before launch-agent apply and persists profile state only after successful apply.
  - Startup workflow regression tests now cover gating, persistence ordering, and manual mount command isolation.
affects: [phase-02-boot-health, phase-03-diagnostics]

# Tech tracking
tech-stack:
  added: []
  patterns: [startup preflight report state in viewmodel, startup toggle ordering preflight->apply->persist, per-profile UI diagnostic snapshots]

key-files:
  created:
    - RcloneMountManager.Tests/ViewModels/MainWindowViewModelStartupTests.cs
  modified:
    - RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs
    - RcloneMountManager.GUI/Views/MainWindow.axaml

key-decisions:
  - "Store startup preflight report state per profile in MainWindowViewModel so users can inspect startup readiness without toggling startup."
  - "Gate startup enable on `CriticalChecksPassed` and log each failed check key/message before returning."
  - "Persist profiles immediately after successful startup enable/disable so startup flags survive app restarts without requiring manual Save changes."

patterns-established:
  - "Startup toggle safety pattern: run preflight, block on critical failures, apply launch-agent change, then persist profile JSON."
  - "Startup diagnostics pattern: expose summary + detailed check text in UI and log each check with severity."

# Metrics
duration: 5m
completed: 2026-02-21
---

# Phase 1 Plan 3: Startup UI Gating Summary

**Startup controls now include explicit preflight diagnostics, and startup toggle operations are preflight-gated with persistence only after successful launch-agent apply.**

## Performance

- **Duration:** 5m
- **Started:** 2026-02-21T19:17:49Z
- **Completed:** 2026-02-21T19:23:21Z
- **Tasks:** 3
- **Files modified:** 3

## Accomplishments
- Added `RunStartupPreflightCommand` and UI wiring so users can run startup safety checks independently of toggle actions.
- Refactored startup toggling to enforce preflight gating and immediate persistence only on successful enable/disable apply.
- Added startup workflow regression tests for blocked enable, successful enable persistence, disable persistence, and manual mount command guard stability.

## Task Commits

Each task was committed atomically:

1. **Task 1: Add explicit startup preflight command and user-visible report state** - `a54beac` (feat)
2. **Task 2: Gate startup enablement and persist only after successful apply** - `1e25ce3` (fix)
3. **Task 3: Add startup workflow ViewModel regression tests** - `388a01d` (test)

**Plan metadata:** pending

## Files Created/Modified
- `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs` - Added startup preflight command/report state and enforced startup toggle ordering + persistence behavior.
- `RcloneMountManager.GUI/Views/MainWindow.axaml` - Added startup preflight button and visible preflight summary/details panel near startup controls.
- `RcloneMountManager.Tests/ViewModels/MainWindowViewModelStartupTests.cs` - Added regression coverage for SAFE-03 gating, BOOT-03 persistence semantics, and BOOT-02 workflow isolation.

## Decisions Made
- Kept startup preflight results attached to each profile selection so report details remain visible when switching profiles.
- Used minimal constructor seams in `MainWindowViewModel` for deterministic startup workflow tests without broad DI refactors.
- Preserved `StartMountCommand` and `StopMountCommand` guard logic, validating behavior by regression tests rather than changing command implementations.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] xUnit/VSTest `ClassName` filter did not discover startup viewmodel tests**
- **Found during:** Task 2 and Task 3 verification
- **Issue:** Plan-specified command `dotnet test ... --filter "ClassName=MainWindowViewModelStartupTests"` returned "No test matches".
- **Fix:** Verified startup tests with `FullyQualifiedName~MainWindowViewModelStartupTests` while preserving plan command execution evidence.
- **Files modified:** None
- **Verification:** `dotnet test RcloneMountManager.slnx --filter "FullyQualifiedName~MainWindowViewModelStartupTests"` passed (4/4)
- **Committed in:** N/A (verification-only adjustment)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** No scope creep; deviation only handled test runner filter compatibility.

## Issues Encountered
- `ClassName` test filters continue to return no matches in this repository's current test adapter configuration.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Startup enablement path now enforces deterministic safety gates and immediate persistence semantics.
- Startup diagnostics surface exact failed checks for user action and future health/diagnostic workflows.

---
*Phase: 01-startup-enablement-and-safety-gates*
*Completed: 2026-02-21*
