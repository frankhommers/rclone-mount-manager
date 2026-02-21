---
phase: 04-per-profile-reliability-policy-presets
plan: 02
subsystem: ui
tags: [reliability, presets, viewmodel, mvvm, persistence]

# Dependency graph
requires:
  - phase: 04-01
    provides: typed reliability preset catalog and per-profile preset identity baseline
provides:
  - deterministic ViewModel command path to apply a selected reliability preset
  - managed-key-only reliability option patching that preserves unrelated mount options
  - selected reliability preset id load/save wiring through existing profiles.json mapping
affects: [phase-04-plan-03, preset-picker-ui, profile-persistence]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - ViewModel-managed preset selection state mirrored to selected profile identity
    - deterministic managed-key remove-then-override patching for reliability options
    - migration-safe persisted preset id mapping with balanced fallback

key-files:
  created: []
  modified:
    - RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs

key-decisions:
  - "Apply reliability presets through an explicit ViewModel command instead of ad-hoc flag edits."
  - "Patch only ReliabilityPolicyPreset.ManagedReliabilityKeys and preserve all unrelated mount option entries."
  - "Persist SelectedReliabilityPresetId in PersistedProfile and normalize missing/unknown values to balanced on load."

patterns-established:
  - "Preset apply pattern: sync current options, remove managed reliability keys, then write selected preset overrides."
  - "Persistence pattern: profile selected preset id flows through LoadProfiles/SaveProfiles with migration-safe fallback."

# Metrics
duration: 2 min
completed: 2026-02-21
---

# Phase 4 Plan 2: Reliability Preset Apply/Persist Summary

**MainWindowViewModel now applies profile reliability presets via a deterministic managed-key patch path and persists selected preset ids through profiles.json reloads.**

## Performance

- **Duration:** 2 min
- **Started:** 2026-02-21T22:20:32Z
- **Completed:** 2026-02-21T22:22:18Z
- **Tasks:** 2
- **Files modified:** 1

## Accomplishments
- Added ViewModel preset catalog/selection state plus `ApplyReliabilityPreset` command that only applies to rclone profiles.
- Implemented deterministic managed-key patching (`remove managed reliability keys` then `write preset overrides`) while preserving unrelated mount options.
- Wired selected preset id through `PersistedProfile`, `LoadProfiles`, and `SaveProfiles`, and included preset-id changes in existing dirty tracking.

## Task Commits

Each task was committed atomically:

1. **Task 1: Add ViewModel preset state and deterministic apply command** - `ba22cd6` (feat)
2. **Task 2: Persist and reload selected preset id in profile mapping** - `bb25a33` (feat)

## Files Created/Modified
- `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs` - Added preset selection/apply command flow and persistence mapping for selected preset id.

## Decisions Made
- Keep preset selection state on `MainWindowViewModel` and synchronize it with `SelectedProfile.SelectedReliabilityPresetId` so apply/persist behavior remains profile-scoped.
- Keep NFS guard behavior explicit by no-oping reliability apply for non-rclone profiles.
- Normalize unknown or missing persisted preset ids to `balanced` using `ReliabilityPolicyPreset.GetByIdOrDefault` during profile load.

## Deviations from Plan

None - plan executed exactly as written.

## Authentication Gates

None.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- POL-02 ViewModel apply/persist wiring is complete and verified by focused MainWindowViewModel tests plus full solution build.
- Ready for `04-03-PLAN.md` to wire preset picker/apply UI interactions and regression coverage.

---
*Phase: 04-per-profile-reliability-policy-presets*
*Completed: 2026-02-21*
