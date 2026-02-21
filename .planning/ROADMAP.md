# Roadmap: Rclone Mount Manager

## Overview

This roadmap delivers the v1 promise: configure mounts once and trust they come back automatically and reliably on macOS login. Phases are derived directly from requirement groupings and dependency order, with reliability primitives delivered before diagnostics and policy tuning. Every v1 requirement is mapped exactly once.

## Phases

### Phase 1 - Startup Enablement and Safety Gates

**Goal:** Users can safely enable and persist auto-mount behavior per profile without breaking manual mount workflows.

**Dependencies:** Existing profile CRUD and manual mount lifecycle capabilities.

**Requirements:** BOOT-01, BOOT-02, BOOT-03, SAFE-01, SAFE-02, SAFE-03

**Success Criteria:**
1. User can enable auto-mount for a profile and it is configured to restore at macOS login.
2. User can disable auto-mount for a profile while manual mount/unmount actions continue to work.
3. User can restart the app or reboot macOS and see startup settings preserved for each profile.
4. User can run preflight checks and see explicit failures for missing binary, invalid mount/cache paths, or unavailable credentials.
5. User cannot enable startup behavior for a profile until critical preflight checks pass.

**Plans:** 3 plans

Plans:
- [x] 01-01-PLAN.md - Build typed startup preflight checks and safety report models.
- [x] 01-02-PLAN.md - Modernize LaunchAgent activation/deactivation with linted plist and explicit failures.
- [x] 01-03-PLAN.md - Wire preflight-gated startup toggle and persistence into ViewModel/UI with regression tests.

### Phase 2 - Boot Health Verification and Live State

**Goal:** Users can trust post-login mount state because health is verified and surfaced as truthful runtime status.

**Dependencies:** Phase 1

**Requirements:** HEAL-01, HEAL-02, OBS-01

**Success Criteria:**
1. After login, user can see whether each auto-mount profile is healthy based on mount usability checks.
2. User can see degraded or failed state when a startup mount does not meet health checks.
3. User can observe live per-profile state transitions (idle, mounting, mounted, failed) during startup and normal operation.

**Plans:** 3 plans

Plans:
- [x] 02-01-PLAN.md - Add typed runtime lifecycle/health models and implement bounded `MountHealthService` with classification tests.
- [x] 02-02-PLAN.md - Wire lifecycle/health state transitions into `MainWindowViewModel` and surface runtime state in the main UI.
- [x] 02-03-PLAN.md - Add startup fan-out verification and periodic live-state refresh loop with deterministic ViewModel tests.

### Phase 3 - Startup Diagnostics and Log Isolation

**Goal:** Users can diagnose startup mount failures quickly from in-app lifecycle evidence.

**Dependencies:** Phase 2

**Requirements:** OBS-02, OBS-03

**Success Criteria:**
1. User can view timestamped logs for startup and mount lifecycle events.
2. User can filter logs by profile and isolate one profile's startup failure path quickly.
3. User can follow a profile's event timeline to understand what failed and when.

**Plans:** 3 plans

Plans:
- [x] 03-01-PLAN.md - Add typed per-profile diagnostics event pipeline with explicit async attribution and bounded retention.
- [x] 03-02-PLAN.md - Implement ViewModel profile/startup filters and deterministic timeline projection for diagnostics isolation.
- [x] 03-03-PLAN.md - Wire diagnostics filter controls and timestamped timeline rendering into the main window UI.

### Phase 4 - Per-Profile Reliability Policy Presets

**Goal:** Users can tune reliability behavior safely through presets instead of raw flag editing.

**Dependencies:** Phase 2

**Requirements:** POL-01, POL-02

**Success Criteria:**
1. User can choose a reliability policy preset per profile from the UI.
2. User can apply policy presets without manually editing raw rclone flags.
3. User can revisit profile settings and confirm the selected preset remains applied.

**Plans:** 3 plans

Plans:
- [ ] 04-01-PLAN.md - Add typed reliability preset catalog and per-profile preset identity model.
- [ ] 04-02-PLAN.md - Implement ViewModel preset apply/persist path with managed-key non-clobber patching.
- [ ] 04-03-PLAN.md - Wire preset picker/apply UI and add apply/persist/reload regression tests.

## Progress

| Phase | Name | Requirements | Status |
|-------|------|--------------|--------|
| 1 | Startup Enablement and Safety Gates | BOOT-01, BOOT-02, BOOT-03, SAFE-01, SAFE-02, SAFE-03 | Complete |
| 2 | Boot Health Verification and Live State | HEAL-01, HEAL-02, OBS-01 | Complete |
| 3 | Startup Diagnostics and Log Isolation | OBS-02, OBS-03 | Complete |
| 4 | Per-Profile Reliability Policy Presets | POL-01, POL-02 | Pending |

---
*Depth: standard*
*Coverage: 13/13 v1 requirements mapped*
