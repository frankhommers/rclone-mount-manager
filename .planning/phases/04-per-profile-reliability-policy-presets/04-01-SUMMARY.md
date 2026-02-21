---
phase: 04-per-profile-reliability-policy-presets
plan: 01
subsystem: core
tags: [reliability, presets, model, mvvm, persistence]

# Dependency graph
requires:
  - phase: 02-boot-health-verification-and-live-state
    provides: profile-scoped runtime state and persisted mount profile baseline
provides:
  - typed immutable reliability preset catalog with stable IDs and managed reliability key scope
  - fallback-safe preset lookup by id with default to balanced
  - per-profile selected reliability preset id state on MountProfile for apply/persist wiring
affects: [phase-04-plan-02, phase-04-plan-03, reliability-policy-ui]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - static typed policy catalog as the source of truth for reliability preset values
    - managed-key boundary pattern so preset application can patch only reliability keys
    - profile-scoped preset identity state for deterministic save/load and UI binding

key-files:
  created:
    - RcloneMountManager.Core/Models/ReliabilityPolicyPreset.cs
  modified:
    - RcloneMountManager.Core/Models/MountProfile.cs

key-decisions:
  - "Model reliability presets as immutable records with stable conservative/balanced/aggressive IDs and typed override dictionaries."
  - "Expose managed reliability scope centrally as ManagedReliabilityKeys for future non-clobber option patching."
  - "Store selected preset identity directly on MountProfile with balanced default to keep policy intent profile-scoped and persistence-ready."

patterns-established:
  - "Preset catalog pattern: static Catalog + ID constants + GetByIdOrDefault helper with balanced fallback."
  - "Profile policy state pattern: SelectedReliabilityPresetId observable property on MountProfile as the single persisted selection source."

# Metrics
duration: 1 min
completed: 2026-02-21
---

# Phase 4 Plan 1: Reliability Preset Foundation Summary

**Typed reliability policy presets now exist as an immutable catalog with managed-key scope, and each mount profile carries an explicit selected preset id defaulting to balanced for deterministic apply/persist behavior.**

## Performance

- **Duration:** 1 min
- **Started:** 2026-02-21T22:12:32Z
- **Completed:** 2026-02-21T22:13:52Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments
- Added `ReliabilityPolicyPreset` model/catalog with stable `conservative`, `balanced`, and `aggressive` ids plus display metadata and typed override values.
- Defined managed reliability key scope (`vfs_cache_mode`, `dir_cache_time`, `attr_timeout`, `retries`, `low_level_retries`, `retries_sleep`) for future non-clobber patch application.
- Added `SelectedReliabilityPresetId` observable state to `MountProfile` with a balanced default so policy intent is profile-scoped and ready for persistence mapping.

## Task Commits

Each task was committed atomically:

1. **Task 1: Add typed reliability preset catalog with managed-key scope** - `0bcb000` (feat)
2. **Task 2: Extend profile model with selected reliability preset id** - `57cfee3` (feat)

## Files Created/Modified
- `RcloneMountManager.Core/Models/ReliabilityPolicyPreset.cs` - New immutable preset model/catalog, managed reliability key set, and fallback-safe id lookup helper.
- `RcloneMountManager.Core/Models/MountProfile.cs` - Added profile-scoped `SelectedReliabilityPresetId` observable property defaulting to `balanced`.

## Decisions Made
- Keep reliability preset values in a typed immutable core model rather than ad-hoc string assembly so ViewModel/UI consume stable IDs and metadata.
- Define managed reliability keys in one core catalog location to constrain future preset apply logic to non-destructive key patching.
- Default profile preset selection to `balanced` in `MountProfile` so new and restored profiles have explicit reliability policy intent.

## Deviations from Plan

None - plan executed exactly as written.

## Authentication Gates

None.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Core reliability preset artifacts required by phase 4 apply/persist flow are now in place and compile cleanly.
- Ready for `04-02-PLAN.md` ViewModel preset apply/persist implementation with managed-key non-clobber patching.

---
*Phase: 04-per-profile-reliability-policy-presets*
*Completed: 2026-02-21*
