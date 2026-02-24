# Persistent Mounts with RC-based Adoption

## Problem

When the app closes, rclone child processes die (or survive unpredictably). On next launch, the app cannot reconnect to mounts started in a previous session. Users must manually re-mount every time they restart the app.

## Solution

Start rclone via `nohup` so it's fully detached from the app process. Use rclone's built-in Remote Control (RC) API on a per-profile port to discover, adopt, monitor, and stop mounts — across app restarts.

## Design

### Starting a mount

Replace the current CliWrap `ListenAsync` approach with a detached process launch:

```
/bin/sh -c 'nohup rclone nfsmount {source} {mountPoint} {flags} --rc --rc-no-auth --rc-addr localhost:{rcPort} > {logFile} 2>&1 &'
```

- The process runs with PPID=1 (launchd), fully detached from the app.
- Stderr/stdout are redirected to `~/Library/Application Support/RcloneMountManager/logs/{profileId}.log`.
- After launching, poll `POST http://localhost:{rcPort}/core/pid` to confirm the process started. Retry for up to 5 seconds.
- On success, record the mount as running.

### RC port assignment

Each mount profile gets a deterministic RC port derived from a hash of its profile ID, mapped to the 50000-59999 range. The port is stored as `RcPort` on the persisted profile for stability.

When auto-assigning, verify the port is not already in use by another profile. Increment until a free slot is found.

RC flags (`--rc`, `--rc-no-auth`, `--rc-addr`) are injected automatically by the service layer — they are not stored in `MountOptions`. If the user has manually configured `--rc-addr` in `MountOptions` or `ExtraOptions`, that takes precedence.

### Profile model changes

| Field | Type | Default | Persisted | Notes |
|---|---|---|---|---|
| `RcPort` | `int` | auto-assigned | yes | Deterministic from profile ID |
| `EnableRemoteControl` | `bool` | `true` | yes | When false, RC is not injected and adoption is disabled |

### App startup: adopt orphans

During `InitializeRuntimeMonitoring`, before the health polling loop starts:

For each mount profile where `EnableRemoteControl` is true:

1. POST `http://localhost:{rcPort}/core/pid` — if it responds, rclone is alive.
2. Verify mount point is active via the system `mount` command.
3. Both alive + mounted → adopt as `Mounted / Healthy`.
4. RC responds but mount point not active → stale rclone, send `core/quit`.
5. Mount point active but RC doesn't respond → external/unmanaged mount, show as `Mounted` with a warning.

### App exit

Do nothing. The rclone processes are detached and survive app exit.

### Stopping a mount

1. POST `http://localhost:{rcPort}/core/quit` with `{"exitCode":0}`.
2. Poll for up to 5 seconds: check that `core/pid` stops responding AND mount point disappears from `mount` output.
3. Fallback: `umount {mountPoint}`, then `kill {pid}` if still alive.

### Log file tailing

The diagnostics panel reads the log file for the selected profile. The log file is append-only and persists across app restarts. The health monitor can parse recent lines for ERROR/CRITICAL severity.

Log files are stored at:
```
~/Library/Application Support/RcloneMountManager/logs/{profileId}.log
```

### RC API endpoints used

| Endpoint | Auth | Method | Purpose |
|---|---|---|---|
| `core/pid` | No | POST | Discovery + adoption |
| `core/stats` | No | POST | Transfer stats, error count |
| `core/quit` | No | POST | Graceful shutdown |
| `vfs/list` | No | POST | Verify VFS is active |
| `vfs/stats` | No | POST | Cache info |

All endpoints use POST. `--rc-no-auth` is required for the no-auth endpoints to work.

### UI changes

- Warning when `EnableRemoteControl` is toggled off: "Without remote control, mounts cannot be adopted after app restart."
- Adopted mounts show the same status as normally started mounts.
- Remove manual `--rc` / `--rc-addr` from MountOptions when auto-managed (migrate on load).

### Generated scripts

Update `GenerateScript` to also include `--rc --rc-no-auth --rc-addr localhost:{rcPort}` and use `nohup ... &` for consistency with the runtime behavior.

### Interaction with StartAtLogin / launchd

The launchd plist approach continues to work independently. Launchd-started mounts will also be adoptable if they use the same RC port (the generated script includes RC flags). This unifies the two mount start paths.
