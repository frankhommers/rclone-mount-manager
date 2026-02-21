# Technology Stack

**Project:** Rclone Mount Manager (macOS-focused)
**Research question:** Standard 2025 stack/tool choices for reliable startup-managed rclone mounts
**Researched:** 2026-02-21

## Recommended Stack

### Core App Runtime
| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| .NET SDK / Runtime | 10.0 LTS (10.0.103 SDK line) | Main desktop runtime | LTS baseline and already aligned with your existing stack; current macOS ARM64/x64 installers are first-party supported. |
| Avalonia UI | 11.3.x stable (12.0 preview optional lab only) | Cross-platform desktop UI | Mature/stable production line; strong adoption in .NET desktop OSS and existing app already built on it. |
| CliWrap | 3.10 | Process orchestration for `rclone` | Still a strong fit for robust async command execution, cancellation, and output capture around external CLIs. |

### macOS Startup & Service Management
| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| launchd LaunchAgent (`~/Library/LaunchAgents` or `/Library/LaunchAgents`) | macOS built-in | Boot/login-triggered user mount process lifecycle | This is the canonical macOS job manager; supports `RunAtLoad`, `KeepAlive`, `StartInterval`, structured stdout/stderr files, and system-managed restart behavior. |
| `launchctl` modern workflow (`bootstrap`, `bootout`, `kickstart`, `print`, `blame`) | macOS built-in | Installation, activation, diagnostics of jobs | Current launchctl model is domain-based and better for reliability/debugging than legacy `load/unload` workflows. |
| SMAppService-backed installation path (optional, app-bundle distribution) | macOS ServiceManagement | User-facing login integration and managed helper registration | Preferred for polished setup UX in signed app bundles; pair with launchd under the hood rather than replacing it. |

### Mount Engine Layer
| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| rclone | 1.72+ (recommend current stable, currently 1.73.x) | Mount implementation and remote abstraction | Active release cadence in 2025-2026, ongoing mount/VFS fixes, and strong RC API support for runtime control/observability. |
| rclone RC server (`--rc`) bound to localhost | Built into rclone | Health checks, mount introspection, controlled operations | Enables structured control over mounts (`mount/listmounts`, `mount/unmount`, `core/stats`) without fragile process scraping. |
| macFUSE | 5.1.x | Primary macOS FUSE backend | Most predictable choice for writable mount behavior with rclone; current releases add newer macOS path (FSKit backend messaging) while preserving broad compatibility. |
| FUSE-T (fallback option) | 1.x | Kext-less fallback backend | Useful for environments avoiding kernel-extension workflows, but has known behavior caveats in rclone docs (modtime/write semantics). |

### Reliability Defaults (rclone profile)
| Setting | Recommended baseline | Why |
|---------|----------------------|-----|
| `--vfs-cache-mode` | `writes` (or `full` for demanding apps) | rclone docs explicitly note mount reliability/compatibility issues when cache mode is off. |
| `--cache-dir` | App-owned absolute path per mount profile | Prevents overlap/corruption risks from shared VFS cache paths. |
| `--daemon-wait` (macOS) | Explicitly tune (e.g. 30-90s based on backend) | On macOS/BSD this is a constant wait, so default may be mismatched for boot timing. |
| `--no-unicode-normalization` | keep default (`false`) on macOS | Recommended by rclone docs to avoid filename normalization issues on macOS. |
| RC auth | Enabled whenever not strictly localhost-only | Hardens control plane if network binding ever changes. |

## Standard 2025 Architecture Choice (Opinionated)

Use a **launchd-first, rclone-RC-supervised** model:

1. App writes validated LaunchAgent plist per mount profile.
2. App activates job via `launchctl bootstrap` (domain-aware).
3. Job starts `rclone mount ... --rc --rc-addr localhost:<port>` with explicit cache/config paths.
4. App health-checks via RC endpoints + mountpoint probes.
5. Recovery uses launchd keepalive/restart and app-level backoff policy (not shell loops).

This is the most reliable mainstream pattern for macOS startup-managed CLI-backed mounts in 2025.

## Alternatives Considered

| Category | Recommended | Alternative | Why Not |
|----------|-------------|-------------|---------|
| Startup orchestration | launchd LaunchAgent | App-only "start at login" toggle without managed agent | Too weak for resilient restart semantics and boot-time observability. |
| Process supervision | launchd + RC health checks | Polling `ps`/PID files only | Fragile and race-prone; lacks semantic mount/job visibility. |
| Mount backend on macOS | macFUSE primary | FUSE-T primary | FUSE-T is promising but rclone docs still call out caveats that hurt correctness-sensitive setups. |
| launchctl interface | `bootstrap/bootout/kickstart` | legacy `load/unload` | Legacy path kept for compatibility; modern domain model is clearer and more deterministic. |
| Mount reliability mode | `--vfs-cache-mode writes/full` | `off` | rclone documents reduced compatibility and retry behavior with cache off. |

## Installation

```bash
# Core app stack (if needed in this repository)
dotnet add package Avalonia
dotnet add package Avalonia.Desktop
dotnet add package CliWrap

# rclone + macFUSE are external runtime dependencies (install outside NuGet)
# - rclone: https://rclone.org/downloads/
# - macFUSE: https://github.com/macfuse/macfuse/releases
```

## Sources

- HIGH: rclone mount docs (macOS mount methods, VFS reliability notes, unicode guidance, daemon-wait behavior) - https://rclone.org/commands/rclone_mount/
- HIGH: rclone RC/API docs (RC server controls, mount endpoints, auth flags) - https://rclone.org/rc/
- HIGH: rclone rcd docs (RC server operational defaults) - https://rclone.org/commands/rclone_rcd/
- HIGH: rclone changelog (release recency and active maintenance in 2025-2026) - https://rclone.org/changelog/
- HIGH: launchd.plist man page (modern keys, KeepAlive cautions, LaunchAgent/Daemon locations, SMAppService BundleProgram note) - https://keith.github.io/xcode-man-pages/launchd.plist.5.html
- HIGH: launchctl man page (modern `bootstrap/bootout/kickstart/print/blame`, legacy caveats) - https://keith.github.io/xcode-man-pages/launchctl.1.html
- MEDIUM: .NET download matrix (current 10.0 LTS versions and macOS support) - https://dotnet.microsoft.com/en-us/download
- MEDIUM: Avalonia repository + releases (current stable 11.3.x and active 12 preview) - https://github.com/AvaloniaUI/Avalonia and https://github.com/AvaloniaUI/Avalonia/releases
- MEDIUM: CliWrap repository + release line (3.10 current, cross-platform process control fit) - https://github.com/Tyrrrz/CliWrap
- MEDIUM: macFUSE project site (5.1.x release line, current direction) - https://macfuse.github.io/
- LOW-MEDIUM: FUSE-T project site (kext-less design and goals; verify behavior per environment) - https://www.fuse-t.org/

## Confidence

| Area | Confidence | Notes |
|------|------------|-------|
| Startup/service stack | HIGH | Backed by current launchd/launchctl man pages and platform conventions. |
| rclone reliability settings | HIGH | Directly supported by current rclone docs and changelog. |
| UI/runtime stack continuity | MEDIUM-HIGH | Strongly supported by current release data and existing project alignment. |
| SMAppService UX recommendation | MEDIUM | Apple JS docs are not directly retrievable here; recommendation anchored by launchd man-page references and standard macOS practice. |
