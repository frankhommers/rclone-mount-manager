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
  - Users can clear all remotes after removing dependent mounts (last-remote deletion unblocked)
  - Active sidebar selection is single-owner across cross-click transitions
  - True empty library state is supported (0 remotes, 0 mounts) without placeholder respawn
  - Blocked remote deletion uses explicit modal dialog with dependency details
  - Mount sidebar shows explicit "No mounts yet" copy when empty
  - Remote sidebar subtitle hides `name:/` placeholders and shows only meaningful target info
  - Save flow now persists remote-editor actions and empty-library deletions deterministically
  - Remotes and mounts empty copy is visually symmetric
  - Clear-all restart path now preserves empty library without default profile reinsertion
  - Remote alias rename now updates default alias-root mount sources while preserving custom paths
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
- Moved primary save actions to top of each editor and allowed deleting the final unreferenced remote.
- Hardened cross-click selection handoff by keeping remembered list selections internally while nulling inactive list selection state.

## Task Commits

1. **Task 2 remediation pass:** `a8bf2bd` (fix)
2. **Task 2 entity split + mount remote gating:** `1dd9bd9` (fix)
3. **Task 2 UX simplification + add-command coverage:** `4f2ccf4` (fix)
4. **Task 2 active-highlight + remote delete guardrails:** `4df2508` (fix)
5. **Task 2 save clarity + remote name sync + message clarity:** `90ae139` (fix)
6. **Task 2 top-save placement + last-remote deletion unblock:** `a4c9dc3` (fix)
7. **Task 2 cross-click active-selection hardening:** `804da4b` (fix)
8. **Task 2 empty-state + modal deletion feedback + rename stability:** `c8d3cf3` (fix)
9. **Task 2 mounts-empty copy + remote subtitle cleanup:** `7f9f8d2` (fix)
10. **Task 2 persistence pipeline hardening + empty-copy symmetry:** `c2429ee` (fix)
11. **Task 2 clear-all restart + alias-sync consistency hardening:** `5194557` (fix)

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
- Avalonia theme `ListBoxItem:selected:pointerover/focus` styles still applied on inactive list, overriding previous neutral-selected override.
- Selection proxies still let both list selection models stay non-null after cross-clicking; style override alone was insufficient to guarantee one-active visual ownership.
- Deletion flow had no explicit dependency guardrails for mount->remote references, making failure mode unclear.
- Deletion feedback omitted dependent mount names, so users could not immediately resolve blocked deletion.
- Remote name input updated backend alias intent but not profile display name, causing sidebar label drift.
- Remove command required `Profiles.Count > 1`, which unintentionally blocked deleting the final remote after mounts were removed.
- Last-profile delete path forced fallback mount insertion, producing ghost mount respawn after clearing everything.
- Remote name edit buffer could be overwritten by backend/profile reseeding, causing visible rename jumps.
- Remote sidebar subtitle bound directly to raw `Source`, exposing confusing alias-root placeholder strings like `remote1:/`.
- `Save remote` action only marked dirty and did not write profiles file, so users perceived saved changes as reverting on restart.
- Empty-library delete flow wrote status but relied on later manual save, so clear-all could be lost after restart.
- Constructor always seeded a default profile when loaded list was empty, so persisted empty-library state was overwritten on restart.
- Remote name editor updates changed alias but dependent mount source stayed stale for generated alias-root sources.

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

**5. [Rule 1 - Bug] Fixed inactive-list selected visual override and last-remote deletion flow**
- **Found during:** Task 2 checkpoint feedback (blocking UAT)
- **Issue:** Inactive list still rendered blue selected state from higher-specificity Avalonia selected pseudo-class variants; final remote removal blocked by global count guard.
- **Fix:** Added inactive-list style overrides for `:selected`, `:selected:pointerover`, and `:selected:focus`; removed global count guard and allowed deleting final unreferenced remote with empty-state fallback mount.
- **Files modified:** `RcloneMountManager.GUI/Views/MainWindow.axaml`, `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`, `RcloneMountManager.Tests/ViewModels/MainWindowViewModelSidebarSelectionTests.cs`
- **Verification:** `dotnet test --filter MainWindowViewModelSidebarSelectionTests`, `dotnet build`
- **Committed in:** `a4c9dc3`

**6. [Rule 1 - Bug] Fixed cross-click dual-highlight by single-owner selection state**
- **Found during:** Task 2 checkpoint feedback (blocking UAT)
- **Issue:** After cross-clicking between lists, both selection models remained non-null and both lists could still render selected visuals.
- **Fix:** Added remembered remote/mount selection storage and enforced single active selection model (`SelectedRemoteProfile` xor `SelectedMountProfile`) at runtime.
- **Files modified:** `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`, `RcloneMountManager.Tests/ViewModels/MainWindowViewModelSidebarSelectionTests.cs`
- **Verification:** `dotnet test --filter MainWindowViewModelSidebarSelectionTests`, `dotnet build`
- **Committed in:** `804da4b`

**7. [Rule 1 - Bug] Removed ghost mount respawn and stabilized remote naming source**
- **Found during:** Task 2 checkpoint feedback (blocking UAT)
- **Issue:** Clearing all entities spawned a fallback mount unexpectedly; remote name field could jump due to reseeding from competing sources.
- **Fix:** Removed fallback insertion for delete-to-empty, enabled explicit empty-library state, and anchored remote editor name to profile name without backend override while editing remotes.
- **Files modified:** `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`, `RcloneMountManager.GUI/Views/MainWindow.axaml`, `RcloneMountManager.Tests/ViewModels/MainWindowViewModelSidebarSelectionTests.cs`
- **Verification:** `dotnet test --filter MainWindowViewModelSidebarSelectionTests`, `dotnet build`
- **Committed in:** `c8d3cf3`

**8. [Rule 2 - Missing Critical] Added modal contract for blocked remote deletion**
- **Found during:** Task 2 checkpoint feedback (blocking UAT)
- **Issue:** Status text alone was not sufficiently visible for destructive-operation block feedback.
- **Fix:** Added dedicated deletion-block modal state and dismiss command with explicit dependent mount details.
- **Files modified:** `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`, `RcloneMountManager.GUI/Views/MainWindow.axaml`, `RcloneMountManager.Tests/ViewModels/MainWindowViewModelSidebarSelectionTests.cs`
- **Verification:** `dotnet test --filter MainWindowViewModelSidebarSelectionTests`, `dotnet build`
- **Committed in:** `c8d3cf3`

**9. [Rule 1 - Bug] Cleaned remote subtitle formatting and added mounts-empty section copy**
- **Found during:** Task 2 checkpoint feedback (blocking UAT)
- **Issue:** Sidebar subtitle displayed confusing alias-root placeholders and mounts section lacked explicit empty copy.
- **Fix:** Added `RemoteSidebarSubtitle` computed display (hide placeholder, show meaningful target only) and `No mounts yet` empty text in mounts list area.
- **Files modified:** `RcloneMountManager.Core/Models/MountProfile.cs`, `RcloneMountManager.GUI/Views/MainWindow.axaml`, `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`, `RcloneMountManager.Tests/ViewModels/MainWindowViewModelSidebarSelectionTests.cs`
- **Verification:** `dotnet test --filter MainWindowViewModelSidebarSelectionTests`, `dotnet build`
- **Committed in:** `7f9f8d2`

**10. [Rule 1 - Bug] Fixed save/restart persistence for remote actions and empty-library clears**
- **Found during:** Task 2 checkpoint feedback (critical)
- **Issue:** Save pipeline did not persist remote-editor saves or empty-library clears reliably.
- **Fix:** Persist immediately after `Save remote`, persist when delete reaches empty library, and keep explicit saved status feedback.
- **Files modified:** `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`, `RcloneMountManager.Tests/ViewModels/MainWindowViewModelSidebarSelectionTests.cs`
- **Verification:** `dotnet test --filter MainWindowViewModelSidebarSelectionTests`, `dotnet build`
- **Committed in:** `c2429ee`

**11. [Rule 1 - Bug] Added symmetric remotes empty copy and stabilized subtitle semantics**
- **Found during:** Task 2 checkpoint feedback
- **Issue:** Empty-list guidance was inconsistent and remote subtitle still surfaced confusing placeholder forms.
- **Fix:** Standardized remotes copy (`No remotes yet`) and constrained subtitle rendering to meaningful path target only.
- **Files modified:** `RcloneMountManager.GUI/Views/MainWindow.axaml`, `RcloneMountManager.Core/Models/MountProfile.cs`, `RcloneMountManager.Tests/ViewModels/MainWindowViewModelSidebarSelectionTests.cs`
- **Verification:** `dotnet test --filter MainWindowViewModelSidebarSelectionTests`, `dotnet build`
- **Committed in:** `c2429ee`

**12. [Rule 1 - Bug] Fixed clear-all restart regression and default alias source sync**
- **Found during:** Task 2 checkpoint feedback (critical)
- **Issue:** Empty saved payload reloaded with seeded default profile; default-generated mount sources did not follow remote alias rename.
- **Fix:** Only seed default profile when profiles file is missing (not when empty payload exists), and propagate remote alias rename to mount sources only when source was alias-root generated.
- **Files modified:** `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`, `RcloneMountManager.Tests/ViewModels/MainWindowViewModelSidebarSelectionTests.cs`
- **Verification:** `dotnet test --filter MainWindowViewModelSidebarSelectionTests`, `dotnet build`
- **Committed in:** `5194557`

---

**Total deviations:** 12 auto-fixed (10 bug, 2 missing critical)
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
