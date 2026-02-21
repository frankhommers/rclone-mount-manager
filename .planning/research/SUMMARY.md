# Project Research Summary

**Project:** Rclone Mount Manager
**Domain:** macOS-first desktop rclone startup mount orchestration
**Researched:** 2026-02-21
**Confidence:** HIGH

## Executive Summary

This is a reliability-first desktop control plane for `rclone mount`, not just a launch helper. The strongest pattern across all research is a launchd-supervised runtime with an app-owned orchestration policy: let macOS handle process lifecycle, and let the app decide readiness, retries, quarantine, and diagnostics. Teams that ship this domain successfully treat startup reliability as a state machine problem, not a single "start command" action.

The recommended approach is a launchd-first, RC-supervised architecture: generate validated per-profile LaunchAgents, start via modern `launchctl bootstrap/bootout/kickstart`, run each mount with localhost RC enabled, and declare success only after layered health checks (process + mount table + RC + lightweight I/O probe). This should be wrapped in deterministic startup sequencing, bounded retry budgets, and per-profile cache isolation.

The biggest delivery risks are false-positive health states, wrong launchd domain/session targeting, and weak diagnostics that make reboot failures non-actionable. Mitigation is clear in the research: explicit domain handling, post-boot verification gates before "Healthy", bounded backoff with quarantine, and durable logs/event timelines exposed to users with guided remediation.

## Key Findings

### Recommended Stack

Use the existing .NET/Avalonia baseline and harden around modern macOS startup semantics and rclone runtime controls. The stack recommendation is conservative where reliability matters most: official LTS/runtime channels, launchd-native startup management, and documented rclone VFS/RC practices.

**Core technologies:**
- `.NET 10.0 LTS`: Desktop runtime baseline already aligned with codebase and long-term support.
- `Avalonia 11.3.x`: Stable cross-platform desktop UI line consistent with existing app structure.
- `CliWrap 3.10`: Robust async process orchestration/cancellation around external CLIs.
- `launchd` + modern `launchctl`: Canonical macOS startup/job lifecycle with better diagnostics than legacy load/unload.
- `rclone 1.72+ (target current stable 1.73.x)`: Active mount engine with RC API for control and observability.
- `macFUSE 5.1.x` (primary), `FUSE-T` (fallback): Primary path for writable mount correctness, fallback for kext-averse setups.

**Critical version/behavior requirements:**
- Keep `--vfs-cache-mode` at least `writes` (or `full` for demanding workloads).
- Enforce per-profile `--cache-dir` isolation.
- Tune `--daemon-wait` explicitly on macOS.
- Keep macOS unicode normalization default (`--no-unicode-normalization=false`).

### Expected Features

The research converges on "configure once, trust after reboot" as the core product promise. MVP should ship a complete setup-to-startup path with readiness validation and failure transparency, not just additional mount toggles.

**Must have (table stakes):**
- Profile management with validated per-remote options.
- Fast mount/unmount with clear real-time state.
- Auto-start + restore mounts at login.
- Startup preflight checks (binary, mount path, cache path, credentials, backend prerequisites).
- Actionable failures with retry controls and visible activity/log timeline.
- Sane cache defaults for compatibility (`writes/full` policies).

**Should have (competitive):**
- Readiness score with one-click fixups.
- Deterministic startup orchestration (queue/order/backoff/dependency gates).
- Post-boot verification plus self-heal loop.
- Guided failure diagnosis mapped to concrete remediation.

**Defer (v2+):**
- In-app reboot simulation harness.
- Fine-grained policy engine beyond preset profiles.
- Team/collaboration policy workflows.

### Architecture Approach

Adopt a supervisor + worker pattern with app-level reliability control. `launchd` supervises per-profile workers; Core services own desired state, reconciliation, health interpretation, and recovery strategy. This should be implemented as explicit components with strong boundaries and idempotent reconcile behavior.

**Major components:**
1. `GUI Control Plane` - profile intent, diagnostics, and operator actions.
2. `Startup Orchestrator` - launchd registration/reconciliation of desired vs actual startup state.
3. `Mount Runtime Manager` - argument construction, process start/stop, identity tracking.
4. `Health Supervisor` - layered health gates (process/mount/RC/probe).
5. `Recovery Engine` - failure classification, bounded retries, quarantine/escalation.
6. `Event/Telemetry Log` - durable lifecycle timeline for supportable failure diagnosis.

### Critical Pitfalls

1. **Wrong launchd domain/session model** - explicitly design/test domain targeting (`system`, `user/<uid>`, `gui/<uid>`) with modern launchctl flows.
2. **Assuming startup trigger means readiness** - do not rely on `RunAtLoad/KeepAlive` for network/auth readiness; add app-side readiness checks and retries.
3. **Treating process spawn as success** - require mount + RC + probe gates before marking healthy.
4. **Shared VFS cache paths across mounts** - enforce per-profile cache directories and validate non-overlap preflight.
5. **No durable diagnostics pipeline** - always persist launchd stdout/stderr plus structured app lifecycle events.

## Implications for Roadmap

Based on dependencies and risk concentration, suggested phase structure:

### Phase 1: Startup Foundation and Safety Gates
**Rationale:** All later reliability work depends on a correct startup substrate and explicit state model.
**Delivers:** Per-profile state machine, launchd domain-aware adapter modernization, startup preflight checks, baseline event logging.
**Addresses:** Unified configure+auto-mount flow prerequisites, startup-safe validation, profile registration lifecycle.
**Avoids:** Wrong domain targeting, hidden startup environment assumptions, silent failures.

### Phase 2: Deterministic Boot Orchestration and Health Verification
**Rationale:** Once startup wiring exists, next priority is turning boot behavior from best-effort to deterministic.
**Delivers:** Ordered/staggered startup queue, bounded retry/backoff with budget, layered post-launch health gates, post-boot reconciliation.
**Addresses:** Auto-mount reliability after reboot, explicit healthy/degraded/failed states.
**Uses:** `launchd` supervision + `rclone --rc` observability + per-profile cache/runtime isolation.
**Avoids:** Startup race conditions, infinite retry loops, false green states.

### Phase 3: Guided Recovery and User Diagnostics UX
**Rationale:** Reliability without understandable remediation will still feel broken in production.
**Delivers:** Failure classification taxonomy, actionable remediation UI, per-profile startup timeline bundle, one-click recovery actions.
**Addresses:** Actionable failure feedback, trust in autonomous startup behavior.
**Implements:** Recovery Engine + diagnostics surfaces in GUI.
**Avoids:** Non-actionable support reports, manual guesswork triage.

### Phase 4: Policy Presets and Scale Hardening
**Rationale:** Once baseline reliability is proven, optimize for workload diversity and larger profile counts.
**Delivers:** Runtime policy presets, adaptive polling/scheduling, profile-group safeguards, expanded observability retention.
**Addresses:** Differentiators (policy presets, smarter startup behavior at scale).
**Avoids:** One-size-fits-all cache/polling regressions and startup storms.

### Phase Ordering Rationale

- Reliability primitives (state, startup, checks, logs) must precede UX polish; otherwise UI reports non-truthful status.
- Architecture boundaries map naturally to phases: startup adapter -> orchestrator/health -> recovery UX -> optimization.
- This order front-loads mitigation of highest-risk pitfalls (domain errors, false health, retry loops).

### Research Flags

Phases likely needing deeper research during planning:
- **Phase 1:** launchd domain/session edge cases per distribution mode (user launch agent vs app-bundle helper/SMAppService).
- **Phase 2:** backend-specific boot readiness heuristics and timeout tuning (`--daemon-wait`, probe strategy) by remote type.
- **Phase 4:** high-profile-count scheduling/telemetry design if targeting 100+ profiles.

Phases with standard patterns (can usually skip extra research-phase):
- **Phase 3:** failure classification + remediation UX patterns are well-established once taxonomy is defined.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | Core recommendations are grounded in official rclone and launchd/launchctl docs; versions are current and practical. |
| Features | MEDIUM-HIGH | Table stakes are strongly corroborated by mature competitors and official behavior docs; differentiators are more opinionated. |
| Architecture | HIGH | Patterns align with proven supervisor-worker models and map directly onto existing codebase seams. |
| Pitfalls | HIGH | Risks are repeatedly documented in official macOS/rclone references and match known failure modes in startup-managed mounts. |

**Overall confidence:** HIGH

### Gaps to Address

- Apple `SMAppService` implementation specifics should be validated against current Apple docs during planning for bundled distribution UX.
- Exact launchd domain selection matrix should be validated on clean reboot/login scenarios across target install modes.
- Probe/readiness thresholds (timeouts, retry budgets) need empirical tuning on representative remotes before locking defaults.
- FUSE-T behavior caveats should be verified per supported workload if offered beyond fallback status.

## Sources

### Primary (HIGH confidence)
- rclone mount docs (`https://rclone.org/commands/rclone_mount/`) - VFS reliability, macOS caveats, daemon behavior.
- rclone RC docs (`https://rclone.org/rc/`, `https://rclone.org/commands/rclone_rcd/`) - runtime control and mount introspection endpoints.
- macOS man pages (`launchd.plist(5)`, `launchctl(1)`) - job semantics, domain model, modern command workflow.

### Secondary (MEDIUM confidence)
- Avalonia releases/docs and CliWrap repo - runtime/UI/process stack continuity and current stable lines.
- Mountain Duck / ExpanDrive / CloudMounter docs - feature expectations and UX baselines.
- Apple archived launchd guide - conceptual model support, requires modern API revalidation.

### Tertiary (LOW confidence)
- FUSE-T project documentation - directional fallback option; requires workload-level validation.
- rclone GitHub issue snapshots - directional evidence of recurring pain points, not normative guidance.

---
*Research completed: 2026-02-21*
*Ready for roadmap: yes*
