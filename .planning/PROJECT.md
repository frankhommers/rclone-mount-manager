# Rclone Mount Manager

## What This Is

Rclone Mount Manager is a desktop app for configuring and running rclone mounts, with a strong focus on reliable automatic mounting at system startup. It targets users who want a simple UI-driven setup instead of hand-writing mount scripts. Initial use is personal, with intent to open source it for broader users.

## Core Value

Users can configure mounts once and trust they are mounted automatically and reliably on boot.

## Requirements

### Validated

- ✓ Desktop UI for creating and editing mount profiles — existing
- ✓ Manual mount lifecycle actions through rclone/mount command orchestration — existing
- ✓ Persisted profile configuration with runtime state tracking — existing

### Active

- [ ] User can configure a mount and boot auto-mount behavior in one clear flow
- [ ] User can verify auto-mount readiness before reboot (validation/preflight)
- [ ] User can reboot and find configured mounts available and usable without manual intervention
- [ ] User can diagnose startup mount failures with clear, actionable feedback

### Out of Scope

- Multi-user/team collaboration features — initial scope is single-user desktop workflow
- Full cloud service orchestration beyond rclone mount lifecycle — not required for core boot-mount reliability goal

## Context

- Existing brownfield codebase already implements a .NET/Avalonia MVVM desktop app split across `RcloneMountManager.GUI` and `RcloneMountManager.Core`.
- Mount operations and OS integration are executed via `CliWrap` wrappers around `rclone`, mount tools, and macOS `launchctl`.
- Current project direction is mixed roadmap work (features + fixes + refactors), but reliability is prioritized over speed.
- v1 target platform is macOS desktop, with success defined by both one-click-like setup experience and reliable reboot behavior.

## Constraints

- **Platform**: macOS desktop first — boot behavior and startup integration must work with macOS process/service model
- **Quality Priority**: Reliability first — correctness and startup robustness take priority over delivery speed

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Prioritize configure + auto-mount flow in v1 | Core user value is setup-once, auto-mount-on-boot | — Pending |
| Target macOS first | Existing runtime/distribution path already centered on macOS | — Pending |
| Optimize for reliability over speed | Startup mount trust is the critical adoption criterion | — Pending |
| Aim for open-source readiness after personal-first use | Immediate user is solo, but design should scale to public users | — Pending |

---
*Last updated: 2026-02-21 after initialization*
