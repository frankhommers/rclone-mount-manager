---
phase: 04-per-profile-reliability-policy-presets
plan: 03
subsystem: ui
tags: [reliability, presets, avalonia, viewmodel, regression-tests]

# Dependency graph
requires:
  - phase: 04-02
    provides: ViewModel preset apply command, managed-key patching, and preset id persistence model
provides:
  - profile-scoped reliability preset picker and explicit apply action in Profile Settings UI
  - deterministic regression tests for apply, non-clobber behavior, and preset persistence/reload
  - final integration verification for policy preset flow via targeted tests and full solution build
affects: [phase-4-completion, policy-preset-ux, regression-safety]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - MainWindow profile settings expose policy selection via SelectedValue id binding + explicit apply command
    - policy preset tests prime MountOptionsViewModel option catalog for deterministic no-startup-load execution

key-files:
  created:
    - RcloneMountManager.Tests/ViewModels/MainWindowViewModelPolicyPresetTests.cs
  modified:
    - RcloneMountManager.GUI/Views/MainWindow.axaml

key-decisions:
  - "Place reliability controls directly in the Profile Settings pane so preset selection remains profile-scoped and adjacent to mount configuration."
  - "Use explicit Apply action bound to ApplyReliabilityPresetCommand rather than implicit apply-on-select behavior."
  - "Prime MountOptionsViewModel option groups in tests when loadStartupData=false so non-clobber assertions remain deterministic."

patterns-established:
  - "UI binding pattern: ReliabilityPresets + SelectedReliabilityPresetId + ApplyReliabilityPresetCommand in one panel."
  - "Persistence regression pattern: apply preset, save profiles.json, reload ViewModel, assert preset id + managed options restored."

# Metrics
duration: 3 min
completed: 2026-02-21
---

# Phase 4 Plan 3: Reliability Preset UI + Regression Summary

**Profile settings now include a reliability preset picker with explicit apply action, backed by deterministic tests that lock apply/non-clobber/persist/reload behavior for per-profile policy tuning.**

## Performance

- **Duration:** 3 min
- **Started:** 2026-02-21T22:24:37Z
- **Completed:** 2026-02-21T22:27:41Z
- **Tasks:** 3
- **Files modified:** 2

## Accomplishments
- Added a dedicated Reliability panel in `MainWindow.axaml` with preset `ComboBox`, profile-scoped selected-value binding, and explicit Apply command button.
- Added `MainWindowViewModelPolicyPresetTests` to cover managed-key apply behavior, preservation of unrelated options, preset-id persistence, and reload restoration.
- Verified final integration with focused preset tests and full solution build without relaxing non-clobber guarantees.

## Task Commits

Each task was committed atomically:

1. **Task 1: Add profile reliability preset picker and apply controls to main window** - `3a47e50` (feat)
2. **Task 2: Add regression tests for apply, persistence, reload, and non-clobber guarantees** - `f14defa` (test)
3. **Task 3: Run targeted and full build verification for final phase integration** - No code changes (verification-only task)

## Files Created/Modified
- `RcloneMountManager.GUI/Views/MainWindow.axaml` - Added reliability policy UI controls and helper guidance for managed preset flow.
- `RcloneMountManager.Tests/ViewModels/MainWindowViewModelPolicyPresetTests.cs` - Added deterministic ViewModel regressions for apply/persist/reload/non-clobber behavior.

## Decisions Made
- Keep reliability controls in Profile Settings (not Actions row) to reinforce profile-scoped policy intent.
- Require explicit apply button for reliability presets so changing selection alone does not mutate mount flags.
- Seed `MountOptionsViewModel` option groups inside tests to avoid startup metadata-loading dependency and keep tests deterministic.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Seeded MountOptionsViewModel option catalog in policy tests**
- **Found during:** Task 2 (regression tests)
- **Issue:** With `loadStartupData=false`, `MountOptionsViewModel` had no loaded option catalog, causing `SyncMountOptionsToProfile()` to drop unrelated options in test setup and fail non-clobber assertions.
- **Fix:** Added test-only priming helper that injects a minimal option catalog via reflection before applying presets.
- **Files modified:** `RcloneMountManager.Tests/ViewModels/MainWindowViewModelPolicyPresetTests.cs`
- **Verification:** `dotnet test RcloneMountManager.slnx --filter "FullyQualifiedName~MainWindowViewModelPolicyPresetTests"`
- **Committed in:** `f14defa` (Task 2)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Auto-fix was necessary to complete deterministic regression coverage without introducing runtime behavior changes.

## Authentication Gates

None.

## Issues Encountered

None beyond the auto-fixed deterministic test setup blocker.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- POL-01 and POL-02 are now fully covered end-to-end across ViewModel behavior, profile settings UI, and persistence/reload regressions.
- Phase 4 execution is complete and ready for roadmap/state closeout.

---
*Phase: 04-per-profile-reliability-policy-presets*
*Completed: 2026-02-21*
