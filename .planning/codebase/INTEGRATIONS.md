# External Integrations

**Analysis Date:** 2026-02-21

## APIs & External Services

**CLI-driven system integrations:**
- rclone CLI - primary integration for mount option discovery, backend discovery, remote creation, connection checks, mount execution, and password obscuring.
  - SDK/Client: `CliWrap` (`RcloneMountManager.Core/Services/RcloneOptionsService.cs`, `RcloneMountManager.Core/Services/RcloneBackendService.cs`, `RcloneMountManager.Core/Services/MountManagerService.cs`).
  - Auth: per-profile credentials and rclone flags (`RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`, `RcloneMountManager.Core/Services/MountManagerService.cs`).
- OS mount tools - filesystem mount/unmount operations via `mount`, `umount`, and Linux `fusermount`.
  - SDK/Client: `CliWrap` (`RcloneMountManager.Core/Services/MountManagerService.cs`).
  - Auth: OS-level user permissions for mount paths and commands (`RcloneMountManager.Core/Services/MountManagerService.cs`).
- macOS launchd - start-at-login integration using `launchctl` and per-profile plist files.
  - SDK/Client: `CliWrap` (`RcloneMountManager.Core/Services/LaunchAgentService.cs`).
  - Auth: local user session permissions (plist under user `~/Library/LaunchAgents` in `RcloneMountManager.Core/Services/LaunchAgentService.cs`).

## Data Storage

**Databases:**
- Not detected.

**File Storage:**
- Local filesystem only.
- Profiles persisted as JSON in app data (`RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`, `_profilesFilePath` -> `ApplicationData/RcloneMountManager/profiles.json`).
- Generated startup scripts stored under app data (`RcloneMountManager.Core/Services/LaunchAgentService.cs`).
- App logs written to `logs/` under app base directory (`RcloneMountManager.GUI/Program.cs`).

**Caching:**
- In-memory cache for selected rclone mount command per binary path (`_rcloneMountCommandCache` in `RcloneMountManager.Core/Services/MountManagerService.cs`).

## Authentication & Identity

**Auth Provider:**
- No centralized application auth provider detected.
- Integration auth is per-protocol/per-remote input through rclone arguments (WebDAV/SFTP/FTP/FTPS fields on profiles in `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs` and argument assembly in `RcloneMountManager.Core/Services/MountManagerService.cs`).

## Monitoring & Observability

**Error Tracking:**
- None detected (no external error tracking service package/config).

**Logs:**
- Structured app logging via Serilog to console and rolling file logs (`RcloneMountManager.GUI/Program.cs`).
- Command/process feedback appended to UI logs in ViewModel (`RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`).

## CI/CD & Deployment

**Hosting:**
- Not applicable (desktop-distributed app, not server-hosted).

**CI Pipeline:**
- GitHub Actions release workflow builds macOS app bundles and DMGs (`.github/workflows/release-dmg.yml`).
- Release asset upload uses `softprops/action-gh-release@v2` (`.github/workflows/release-dmg.yml`).

## Environment Configuration

**Required env vars:**
- Application runtime env vars: none required by source code (no `Environment.GetEnvironmentVariable` usage detected in `*.cs`).
- Generated scripts may require password env vars when secure script mode is used: `WEBDAV_PASSWORD`, `SFTP_PASSWORD`, `FTP_PASSWORD` (`BuildScriptPasswordValue` in `RcloneMountManager.Core/Services/MountManagerService.cs`).

**Secrets location:**
- Quick-connect credentials are stored in profile JSON payload (`PersistedProfile.QuickConnectPassword` in `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`).
- Script-time secrets can be injected via shell env vars when `AllowInsecurePasswordsInScript` is false (`RcloneMountManager.Core/Services/MountManagerService.cs`).

## Webhooks & Callbacks

**Incoming:**
- None detected.

**Outgoing:**
- None detected.

---

*Integration audit: 2026-02-21*
