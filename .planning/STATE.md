# State: Rclone Mount Manager

## Project Reference

**Core Value:** Users can configure mounts once and trust they are mounted automatically and reliably on boot.

**Current Focus:** Phase 4 complete with 04-04 checkpoint gap closed; remotes/mounts separation, persistence, and sidebar UX are verified.

## Current Position

- **Current Phase:** 4 of 4 - Per-Profile Reliability Policy Presets
- **Current Plan:** 4 of 4 completed in phase 4 (`04-04-PLAN.md`)
- **Overall Status:** Phase 1 through phase 4 complete
- **Last activity:** 2026-02-22 - Completed `04-04-PLAN.md`
- **Progress:** [██████████] 13/13 plans complete (100%)

## Performance Metrics

- **Roadmap depth:** standard
- **v1 requirements:** 13
- **Mapped requirements:** 13
- **Coverage:** 100%
- **Completed phases:** 4
- **Completed plans:** 13/13 (phase 1 through phase 4 complete)

## Accumulated Context

### Decisions

- Sequence reliability work from startup safety to health truth, then diagnostics, then policy tuning.
- Keep macOS-first startup behavior as the primary delivery focus.
- Use `launchctl bootstrap/bootout` with explicit `gui/<uid>` targeting for startup registration.
- Require `plutil -lint` and strict command exit validation before marking startup registration successful.
- Use machine-readable startup preflight check keys (`binary`, `mount-path`, `cache-path`, `credentials`) for deterministic gating.
- Treat missing explicit cache path as warning and invalid configured cache paths as critical failures.
- Convert invalid preflight path inputs into typed critical results rather than throwing.
- Keep startup preflight reports visible per profile so users can inspect startup readiness before toggling startup.
- Enforce startup toggle order `preflight -> gate -> launch-agent apply -> persist` and only persist after successful apply.
- Treat mounted-but-unusable and probe-timeout outcomes as `Degraded`, reserving `Failed` for mount absence or command-level failures.
- Keep typed runtime state as the source of truth while synchronizing `IsMounted`, `IsRunning`, and `LastStatus` for compatibility.
- Set mount action lifecycle transitions explicitly (`Mounting` before start, `Idle` after confirmed stop, `Failed` on command exceptions).
- Surface lifecycle and health directly in the main UI for selected profile and profile list rows.
- Keep status text derived from typed runtime state formatting, not boolean status concatenation.
- Trigger runtime monitoring at app startup after main window ViewModel initialization.
- Refresh runtime state continuously with a cancellation-safe 3-second monitoring cadence.
- Keep runtime monitoring tests deterministic by injecting refresh wait and batch verification seams.
- Model lifecycle diagnostics as typed `ProfileLogEvent` records with enum-backed category/stage/severity semantics.
- Route async lifecycle logging through captured `profile.Id` context to prevent attribution drift when selection changes.
- Keep diagnostics retention bounded at 250 entries per profile and project typed events to display strings only at the ViewModel edge.
- Keep diagnostics filtering explicit and independent from `SelectedProfile` by using dedicated filter state (`SelectedDiagnosticsProfileId`, `StartupTimelineOnly`).
- Recompute visible diagnostics timeline from typed events on every filter/event/profile input change using stable ordering for deterministic analysis.
- Define startup-only timeline scope by `ProfileLogCategory.Startup` to include startup verification/init events while excluding manual and runtime refresh noise.
- Keep diagnostics panel controls (profile scope + startup-only toggle) directly in the main window timeline area for fast startup-failure isolation.
- Project diagnostics into typed UI rows (`DiagnosticsTimelineRow`) with explicit timestamp/severity/stage/message fields while retaining compatibility display strings.
- Surface an explicit diagnostics empty state (`No diagnostics for current filter.`) instead of blank timeline output.
- Model reliability presets as immutable records with stable `conservative`/`balanced`/`aggressive` IDs and typed override dictionaries.
- Expose managed reliability preset scope centrally through `ManagedReliabilityKeys` for controlled non-clobber option patching.
- Store selected reliability policy intent per profile via `SelectedReliabilityPresetId` with a `balanced` default.
- Apply reliability presets through an explicit ViewModel command path (`ApplyReliabilityPreset`) instead of manual raw flag edits.
- Preserve non-policy mount settings by patching only managed reliability keys (remove managed keys, then apply preset overrides).
- Persist and reload `SelectedReliabilityPresetId` via `profiles.json` mapping with migration-safe fallback to `balanced`.
- Keep reliability policy controls in Profile Settings with profile-scoped selected-value binding to `SelectedReliabilityPresetId`.
- Require explicit `ApplyReliabilityPresetCommand` action rather than implicit apply-on-selection changes.
- Prime `MountOptionsViewModel` option catalog in tests when startup loading is disabled to keep non-clobber regressions deterministic.
- Keep REMOTES and MOUNTS as explicit separate entities with one active sidebar highlight owner at a time.
- Allow true empty library persistence (0 remotes, 0 mounts) and do not reseed defaults when saved payload is empty.
- Block remote deletion with explicit dependency modal feedback listing dependent mount names/count.
- Update dependent mount source aliases only when sources use generated alias-root form during remote alias rename.

### TODOs

- None.

### Blockers

- None.

## Session Continuity

- **Last completed artifact:** `.planning/phases/04-per-profile-reliability-policy-presets/04-04-SUMMARY.md`
- **Last updated files:** `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`, `RcloneMountManager.GUI/Views/MainWindow.axaml`, `RcloneMountManager.Tests/ViewModels/MainWindowViewModelSidebarSelectionTests.cs`, `.planning/phases/04-per-profile-reliability-policy-presets/04-04-SUMMARY.md`, `.planning/STATE.md`
- **Last session:** 2026-02-22T21:17:08Z
- **Stopped at:** Completed `04-04-PLAN.md`
- **Resume file:** None
- **Next command:** None

---
*Initialized: 2026-02-21*
