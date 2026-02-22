---
phase: 04-per-profile-reliability-policy-presets
plan: 04
subsystem: ui
tags: [avalonia, sidebar, remotes, mounts, reliability-presets, tests]
requires:
  - phase: 04-per-profile-reliability-policy-presets
    provides: reliability preset apply/persist behavior and profile settings surface
provides:
  - Separate REMOTES and MOUNTS entities with explicit add actions
  - Independent sidebar selections for remotes and mounts
  - Single active sidebar highlight while preserving remembered selections
  - Mount-to-remote association gating before save and mount actions
  - Deterministic remote deletion blocking when mounts depend on remote alias
  - Remote name field synchronizes immediately with REMOTES sidebar labels
affects: [phase-4-acceptance, ui-ux, profile-persistence]
tech-stack:
  added: []
  patterns:
    - Discriminator-based sidebar entities (`IsRemoteDefinition`) with dedicated collections
    - Mount save/actions gated on resolved remote association
key-files:
  created: []
  modified:
    - RcloneMountManager.Core/Models/MountProfile.cs
    - RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs
    - RcloneMountManager.GUI/Views/MainWindow.axaml
    - RcloneMountManager.Tests/ViewModels/MainWindowViewModelSidebarSelectionTests.cs
key-decisions:
  - "Treat remotes as first-class sidebar entities with dedicated add flow."
  - "Require rclone mounts to resolve to an existing remote alias before save."
  - "Keep remote and mount selection state independent and never auto-mirror across lists."
  - "Show only one active (blue) sidebar selection at a time to reduce dual-selection confusion."
  - "Use explicit action labels (Save remote, Save mount) to remove persistence ambiguity."
patterns-established:
  - "Sidebar separation: REMOTES and MOUNTS use distinct collections and selection state."
  - "Validation gate: Save/actions blocked when mount lacks remote association."
duration: in-progress checkpoint flow
completed: 2026-02-22
---

# Phase 4 Plan 4: Runtime Verification Gap Closure Summary

**Sidebar UX now uses explicit plus affordances, single active selection highlighting, clear save actions, deterministic remote deletion rules, and immediate remote-name sidebar sync.**

## Performance

- **Duration:** in-progress (checkpoint-driven)
- **Started:** 2026-02-22T14:33:40Z
- **Completed:** 2026-02-22T00:00:00Z
- **Tasks:** 2/2 implemented, pending human approval checkpoint
- **Files modified:** 4

## Accomplishments

- Implemented explicit `Add remote` and `Add mount` actions with separate sidebar entity collections.
- Decoupled REMOTES and MOUNTS selection state so selection in one list does not overwrite the other.
- Enforced remote association for mount save/action flow and added deterministic "in-use remote" deletion blocking.
- Reduced sidebar confusion by keeping remembered selections but showing only one active list highlight at a time.
- Clarified persistence affordances with context-specific action labels (`Save remote`, `Save mount`).
- Synced remote name input to sidebar label updates immediately.

## Task Commits

1. **Task 2 remediation pass:** `a8bf2bd` (fix)
2. **Task 2 entity split + mount remote gating:** `1dd9bd9` (fix)
3. **Task 2 UX simplification + add-command coverage:** `4f2ccf4` (fix)
4. **Task 2 active-highlight + remote delete guardrails:** `4df2508` (fix)
5. **Task 2 save clarity + remote name sync + message clarity:** `90ae139` (fix)

## Files Created/Modified

- `RcloneMountManager.Core/Models/MountProfile.cs` - Adds `IsRemoteDefinition` discriminator for remotes vs mounts.
- `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs` - Separate entity collections, independent selection sync, remote association validation.
- `RcloneMountManager.GUI/Views/MainWindow.axaml` - Adds explicit REMOTES and MOUNTS add actions, mount remote picker, simplified remote create label.
- `RcloneMountManager.Tests/ViewModels/MainWindowViewModelSidebarSelectionTests.cs` - Regression suite for independent selection, separate add flows, and remote-required mount save.

## Decisions Made

- Split sidebar behavior by explicit remote/mount entity role instead of inferring from shared list semantics.
- Keep mount source remote alias linked to selected remote and block save when unresolved.
- Use simple, single-purpose labels and controls to avoid dual-purpose mental model.
- Preserve remembered selection per list but render only active-context selection in sidebar visuals.
- Block deleting remotes that are referenced by mount sources and surface explicit user-facing reason.

## Root Cause

- Earlier wiring treated remotes and mounts as behavior modes over overlapping profile state, causing conceptual and selection coupling.
- UI copy and control placement still implied mixed responsibilities (remote creation "and use in selected profile").
- Sidebar bindings rendered both remembered selections as active at the same time, producing two blue highlights.
- Deletion flow had no explicit dependency guardrails for mount->remote references, making failure mode unclear.
- Deletion feedback omitted dependent mount names, so users could not immediately resolve blocked deletion.
- Remote name input updated backend alias intent but not profile display name, causing sidebar label drift.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed cross-list selection coupling semantics**
- **Found during:** Task 2 checkpoint feedback
- **Issue:** Selection updates leaked across lists via shared selection synchronization assumptions.
- **Fix:** Isolated list selections and synchronization by explicit entity role.
- **Files modified:** `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`, `RcloneMountManager.Tests/ViewModels/MainWindowViewModelSidebarSelectionTests.cs`
- **Verification:** `dotnet test --filter MainWindowViewModelSidebarSelectionTests`
- **Committed in:** `1dd9bd9`

**2. [Rule 2 - Missing Critical] Added mount-remote association gate**
- **Found during:** Task 2 checkpoint feedback
- **Issue:** New mounts could exist without resolvable remote link, violating expected model.
- **Fix:** Added remote association validation for save/mount actions and mount remote selector.
- **Files modified:** `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`, `RcloneMountManager.GUI/Views/MainWindow.axaml`, `RcloneMountManager.Tests/ViewModels/MainWindowViewModelSidebarSelectionTests.cs`
- **Verification:** `dotnet test --filter MainWindowViewModelSidebarSelectionTests`
- **Committed in:** `1dd9bd9`

**3. [Rule 2 - Missing Critical] Added active-highlight and remote-delete UX guardrails**
- **Found during:** Task 2 checkpoint feedback (blocking UAT)
- **Issue:** Simultaneous active highlights and ambiguous removal semantics created operator confusion and blocked remote cleanup.
- **Fix:** Bound sidebar selection visuals to active context proxy properties and blocked remote deletion when referenced by mounts with explicit status message.
- **Files modified:** `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`, `RcloneMountManager.GUI/Views/MainWindow.axaml`, `RcloneMountManager.Tests/ViewModels/MainWindowViewModelSidebarSelectionTests.cs`
- **Verification:** `dotnet test --filter MainWindowViewModelSidebarSelectionTests`, `dotnet build`
- **Committed in:** `4df2508`

**4. [Rule 1 - Bug] Fixed remote name sidebar sync and deletion feedback detail**
- **Found during:** Task 2 checkpoint feedback (blocking UAT)
- **Issue:** Remote-name edits did not immediately align with REMOTES sidebar labels; deletion block feedback lacked actionable detail.
- **Fix:** Synced `NewRemoteName` changes into selected remote profile name and expanded block message with dependent mount names/count.
- **Files modified:** `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`, `RcloneMountManager.Tests/ViewModels/MainWindowViewModelSidebarSelectionTests.cs`
- **Verification:** `dotnet test --filter MainWindowViewModelSidebarSelectionTests`, `dotnet build`
- **Committed in:** `90ae139`

---

**Total deviations:** 4 auto-fixed (2 bug, 2 missing critical)
**Impact on plan:** Deviations were required to satisfy checkpoint-defined acceptance semantics for sidebar entity separation.

## Issues Encountered

- Parallel verification commands intermittently hit an Avalonia PDB file lock; resolved by running required test commands sequentially.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Implementation is ready for final human runtime verification focused on remotes/mounts UX semantics.
- No technical blockers; awaiting user checkpoint approval.

---
*Phase: 04-per-profile-reliability-policy-presets*
*Completed: 2026-02-22*
