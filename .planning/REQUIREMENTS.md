# Requirements: Rclone Mount Manager

**Defined:** 2026-02-21
**Core Value:** Users can configure mounts once and trust they are mounted automatically and reliably on boot.

## v1 Requirements

Requirements for initial release. Each maps to roadmap phases.

### Startup Automation

- [x] **BOOT-01**: User can enable auto-mount for a profile so it restores automatically at macOS login
- [x] **BOOT-02**: User can disable auto-mount for a profile without affecting manual mount capability
- [x] **BOOT-03**: User can persist startup settings so they survive app restarts and reboots

### Startup Safety

- [x] **SAFE-01**: User can run preflight checks before enabling auto-mount
- [x] **SAFE-02**: User sees explicit preflight failures for missing binary, invalid mount path, invalid cache path, or unavailable credentials
- [x] **SAFE-03**: User can only enable startup behavior when critical preflight checks pass

### Boot Verification

- [x] **HEAL-01**: User can see whether each auto-mount profile is healthy after login based on mount usability checks
- [x] **HEAL-02**: User can see when an auto-mount is in degraded or failed state after boot

### Observability

- [x] **OBS-01**: User can see live per-profile mount status (idle, mounting, mounted, failed)
- [ ] **OBS-02**: User can view timestamped logs for startup and mount lifecycle events
- [ ] **OBS-03**: User can filter logs by profile to diagnose startup failures quickly

### Runtime Policy

- [ ] **POL-01**: User can choose a per-profile policy preset for mount reliability behavior
- [ ] **POL-02**: User can apply policy presets without manually editing raw rclone flags

## v2 Requirements

Deferred to future release. Tracked but not in current roadmap.

### Setup UX

- **SET-01**: User can complete profile creation and auto-mount enablement in a guided wizard flow

### Recovery UX

- **REC-01**: User can trigger guided one-click recovery actions from categorized startup failure messages

## Out of Scope

Explicitly excluded. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| Team collaboration and multi-user policy management | Initial target is single-user desktop reliability |
| Full reboot simulation harness in-app | Useful but higher complexity than current reliability baseline |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| BOOT-01 | Phase 1 | Complete |
| BOOT-02 | Phase 1 | Complete |
| BOOT-03 | Phase 1 | Complete |
| SAFE-01 | Phase 1 | Complete |
| SAFE-02 | Phase 1 | Complete |
| SAFE-03 | Phase 1 | Complete |
| HEAL-01 | Phase 2 | Complete |
| HEAL-02 | Phase 2 | Complete |
| OBS-01 | Phase 2 | Complete |
| OBS-02 | Phase 3 | Pending |
| OBS-03 | Phase 3 | Pending |
| POL-01 | Phase 4 | Pending |
| POL-02 | Phase 4 | Pending |

**Coverage:**
- v1 requirements: 13 total
- Mapped to phases: 13
- Unmapped: 0

---
*Requirements defined: 2026-02-21*
*Last updated: 2026-02-21 after phase 2 completion*
