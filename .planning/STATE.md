# State: Rclone Mount Manager

## Project Reference

**Core Value:** Users can configure mounts once and trust they are mounted automatically and reliably on boot.

**Current Focus:** Execute remaining phase 3 plans to build startup diagnostics timeline isolation and observability UX on top of typed runtime state.

## Current Position

- **Current Phase:** 3 - Startup Diagnostics and Log Isolation
- **Current Plan:** 1 of 3 complete in phase 3
- **Overall Status:** Phase 1 and phase 2 complete and verified; phase 3 in progress
- **Last activity:** 2026-02-21 - Completed 03-01-PLAN.md
- **Progress:** [████████░░] 7/9 plans complete (78%)

## Performance Metrics

- **Roadmap depth:** standard
- **v1 requirements:** 13
- **Mapped requirements:** 13
- **Coverage:** 100%
- **Completed phases:** 2
- **Completed plans:** 7/9 (phase 3 plan 1 complete)

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

### TODOs

- Execute `03-02-PLAN.md` (startup diagnostics filtering/projection UI work).
- Execute `03-03-PLAN.md` (phase 3 completion and hardening).

### Blockers

- None.

## Session Continuity

- **Last completed artifact:** `.planning/phases/03-startup-diagnostics-and-log-isolation/03-01-SUMMARY.md`
- **Last updated files:** `RcloneMountManager.Core/Models/ProfileLogEvent.cs`, `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`, `RcloneMountManager.Tests/ViewModels/MainWindowViewModelDiagnosticsTests.cs`, `.planning/phases/03-startup-diagnostics-and-log-isolation/03-01-SUMMARY.md`, `.planning/STATE.md`
- **Last session:** 2026-02-21T21:45:19Z
- **Stopped at:** Completed 03-01-PLAN.md
- **Resume file:** None
- **Next command:** `/gsd-execute-plan 03-02`

---
*Initialized: 2026-02-21*
