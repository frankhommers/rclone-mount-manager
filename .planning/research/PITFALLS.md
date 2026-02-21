# Domain Pitfalls

**Domain:** rclone mount manager startup reliability (macOS-first)
**Researched:** 2026-02-21

## Critical Pitfalls

Mistakes that cause flaky auto-mount behavior, hard-to-debug failures, or redesigns.

### Pitfall 1: Wrong launchd domain/session model
**What goes wrong:** Teams install jobs in the wrong context (for example, system domain when user-session mounts are needed), or use old `load/unload` assumptions.
**Why it happens:** launchd domain/session behavior is non-trivial (`system`, `user/<uid>`, `gui/<uid>`), and many examples online are legacy.
**Consequences:** Mount process starts but cannot access expected user context (config, keychain/session resources), or runs in the wrong lifecycle window.
**Prevention:** Treat domain selection as a first-class design decision; use modern `launchctl bootstrap/bootout/kickstart` flows and explicit domain targets.
**Detection:** Service appears loaded but expected user-visible mount is missing after login/reboot; different behavior between terminal run and launchd run.

### Pitfall 2: Assuming `RunAtLoad`/`KeepAlive` imply network readiness
**What goes wrong:** Jobs start at login/boot and immediately fail because remote/network path is not truly ready.
**Why it happens:** Teams rely on simplistic startup triggers; launchd's old `NetworkState` key is no longer implemented.
**Consequences:** Repeated early failures, startup races, and user distrust in "auto-mount works after reboot".
**Prevention:** Implement explicit readiness checks and bounded retry/backoff in app logic (network reachability, remote auth/config presence, mountpoint state) instead of relying on launchd magic.
**Detection:** First attempt after boot fails, later manual retry succeeds.

### Pitfall 3: Treating process spawn as mount success
**What goes wrong:** App records success as soon as launchd/rclone process starts.
**Why it happens:** Startup orchestration tracks PID/exit, not mount usability.
**Consequences:** False-positive "mounted" state while mountpoint is unusable or not attached.
**Prevention:** Require post-launch readiness probes (mount exists, directory listing/read probe succeeds) before marking profile healthy. Persist a distinct state model: `Scheduled -> Starting -> Mounted -> Degraded/Failed`.
**Detection:** UI says mounted, but Finder/CLI cannot access files; failures cluster around reboot timing.

### Pitfall 4: Unsafe VFS cache strategy across mounts
**What goes wrong:** Multiple rclone instances/mounts share overlapping VFS cache paths.
**Why it happens:** Cache location treated as global default rather than per-mount resource.
**Consequences:** Data corruption risk and hard-to-reproduce behavior under concurrent startup.
**Prevention:** Allocate unique cache directories per mount profile; validate non-overlap during preflight.
**Detection:** Intermittent corruption-like symptoms only when two profiles start together.

### Pitfall 5: No durable diagnostics pipeline
**What goes wrong:** Startup failures leave little or no evidence.
**Why it happens:** Missing `StandardOutPath`/`StandardErrorPath`, missing structured app-side event logs, or logs only in ephemeral console output.
**Consequences:** Teams cannot distinguish config errors, timing races, permissions failures, or mount tool crashes.
**Prevention:** Always wire launchd stdout/stderr to files, collect lifecycle events in app logs, and expose a per-profile "last startup attempt" diagnostic bundle.
**Detection:** Support workflow requires guesswork; reboot-related bug reports are non-actionable.

## Moderate Pitfalls

### Pitfall 1: Filesystem-trigger based orchestration (`WatchPaths`, `PathState`)
**What goes wrong:** Startup logic relies on file-system trigger keys as dependency management.
**Prevention:** Avoid these as primary coordination mechanisms; launchd itself warns they are race-prone/lossy. Prefer explicit IPC/state checks.

### Pitfall 2: Ignoring unmount/recovery edge cases
**What goes wrong:** Busy or stale mountpoints are not handled on next startup.
**Prevention:** Add startup hygiene: detect stale mounts, attempt clean unmount, then bounded forced recovery path with clear user messaging.

### Pitfall 3: Using deprecated launchctl workflows in automation
**What goes wrong:** Tooling depends on legacy `load/unload/list` behavior and brittle output parsing.
**Prevention:** Use modern commands (`bootstrap`, `bootout`, `print`, `print-disabled`, `kickstart`) and treat output as human-oriented diagnostics, not stable API.

## Minor Pitfalls

### Pitfall 1: macOS filename normalization surprises
**What goes wrong:** Duplicate/inconsistent file behavior appears across clients due to Unicode normalization mismatches.
**Prevention:** Keep rclone's macOS recommendation (`--no-unicode-normalization=false`) unless there is a tested reason to change it.

### Pitfall 2: Mis-tuned caching/timeout defaults without validation
**What goes wrong:** Teams tweak `--daemon-wait`, `--attr-timeout`, or VFS settings prematurely and introduce startup or consistency regressions.
**Prevention:** Start from documented defaults, then tune from measured evidence in reboot tests.

## Phase-Specific Warnings

| Phase Topic | Likely Pitfall | Mitigation |
|-------------|---------------|------------|
| Startup integration | Wrong launchd domain/session | Explicit domain matrix (system/user/gui), tested on clean reboot + fresh login |
| Auto-mount orchestration | Startup trigger interpreted as readiness | Add health gate probes before state becomes "Mounted" |
| Reliability hardening | Network race at boot/login | Internal retry/backoff + readiness checks; do not rely on `NetworkState` |
| Multi-profile support | Shared VFS cache paths | Enforce per-profile cache isolation and preflight validation |
| Diagnostics UX | Missing actionable logs | Capture launchd stdout/stderr + app event timeline + user-facing failure reason |

## Sources

- HIGH: Local macOS man page `launchd.plist(5)` (`man launchd.plist`, Darwin 2019) - session/domain model, discouraged keys, race-prone warnings.
- HIGH: Local macOS man page `launchctl(1)` (`man launchctl`, Darwin 2014) - modern domain targeting and `bootstrap/bootout/kickstart` workflow.
- HIGH: rclone mount documentation - https://rclone.org/commands/rclone_mount/ (daemon behavior, macOS caveats, VFS/cache reliability notes).
- MEDIUM: macFUSE mount options wiki - https://github.com/macfuse/macfuse/wiki/Mount-Options (timeouts, permission/access behavior, cache caveats; community-maintained but official project wiki).
- LOW: rclone issue search snapshot - https://github.com/rclone/rclone/issues?q=launchd+mount+macos (used only as directional evidence of recurring mount reliability incidents).
