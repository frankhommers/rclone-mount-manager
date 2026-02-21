# State: Rclone Mount Manager

## Project Reference

**Core Value:** Users can configure mounts once and trust they are mounted automatically and reliably on boot.

**Current Focus:** Phase 3 execution complete; diagnostics observability and startup timeline isolation are delivered and verified.

## Current Position

- **Current Phase:** 3 - Startup Diagnostics and Log Isolation
- **Current Plan:** 3 of 3 complete in phase 3
- **Overall Status:** Phase 1, phase 2, and phase 3 complete and verified
- **Last activity:** 2026-02-21 - Completed 03-03-PLAN.md
- **Progress:** [██████████] 9/9 plans complete (100%)

## Performance Metrics

- **Roadmap depth:** standard
- **v1 requirements:** 13
- **Mapped requirements:** 13
- **Coverage:** 100%
- **Completed phases:** 3
- **Completed plans:** 9/9 (phase 3 complete)

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

### TODOs

- None.

### Blockers

- None.

## Session Continuity

- **Last completed artifact:** `.planning/phases/03-startup-diagnostics-and-log-isolation/03-03-SUMMARY.md`
- **Last updated files:** `RcloneMountManager.GUI/Views/MainWindow.axaml`, `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`, `RcloneMountManager.Tests/ViewModels/MainWindowViewModelDiagnosticsTests.cs`, `.planning/phases/03-startup-diagnostics-and-log-isolation/03-03-SUMMARY.md`, `.planning/STATE.md`
- **Last session:** 2026-02-21T21:55:53Z
- **Stopped at:** Completed 03-03-PLAN.md
- **Resume file:** None
- **Next command:** None

---
*Initialized: 2026-02-21*
