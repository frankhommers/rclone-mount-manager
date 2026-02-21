# Remote Control (RC) Group Design

## Problem

Rclone's Remote Control API (`mount/listmounts`, `mount/unmount`, etc.) enables live mount status monitoring and management. However, the RC server must be explicitly enabled with `--rc` and configured with a listen address. Currently the app does not expose RC options in the UI.

## Design

### New Expander Group

Add "Remote Control" as the 6th Expander group in mount options, alongside Mount, VFS Cache, NFS, Filters, and General.

**Info banner** at the top of the Expander:
> Enable Remote Control to allow mount status monitoring and management.

### Option Selection

Source: the `"rc"` key in `rclone rc --loopback options/info` output. Exclude all `metrics_*` options.

**Basic options** (5 options, shown by default):

| Option | Type | Purpose |
|--------|------|---------|
| `rc` | bool | Enable the RC server |
| `rc_addr` | stringArray | Address:port to bind (e.g., `localhost:5572`) |
| `rc_user` | string | Username for authentication |
| `rc_pass` | string | Password for authentication |
| `rc_no_auth` | bool | Disable authentication requirement |

**Advanced options** (all other `rc_*` options): TLS certificates, web GUI, CORS, htpasswd, timeouts, etc. Visible when "Show Advanced" is toggled on.

### Advanced Flag Override

Rclone marks all RC options as `Advanced: false`. The service must override this: options not in the basic whitelist above are forced to `Advanced = true`.

Implementation: after parsing the `"rc"` group, iterate options and set `Advanced = true` for any option whose name is not in the basic set `{ "rc", "rc_addr", "rc_user", "rc_pass", "rc_no_auth" }`.

### Auto-Populate on New Profile

When a new profile is created:
1. Set `rc` to `true`
2. Set `rc_addr` to `localhost:{port}` where `{port}` is a randomly chosen free port (range: 15572-16572, check availability)
3. These appear as pre-filled values in the RC group; the user can modify them

The chosen port is persisted as part of the profile's mount options so the app knows where to connect for status queries.

### Filtering metrics_* Options

Options starting with `metrics_` are excluded entirely. They relate to Prometheus metrics endpoints, not RC mount management.

Implementation: filter in `RcloneOptionsService` when building the RC group's option list.

## Implementation Changes

### RcloneOptionsService

1. Add `("rc", "Remote Control")` to `MountRelevantGroups`
2. After parsing RC options: filter out `metrics_*` names, override `Advanced` flag for non-essential options
3. New constant: `RcBasicOptions = { "rc", "rc_addr", "rc_user", "rc_pass", "rc_no_auth" }`

### MountOptionsViewModel

1. Info banner: add a `Description` or `InfoText` property to `MountOptionGroupViewModel` (null for other groups, set for RC)
2. XAML: conditionally show info text above options list in the Expander

### MountProfile / MainWindowViewModel

1. Auto-populate logic: when creating a new profile, pre-fill `rc` and `rc_addr` in the default mount options
2. Port selection: utility method to find a free TCP port in range

### Script Generation

No changes needed. RC options flow through the existing `GenerateScript()` pipeline since they are standard mount options stored in `MountOptions`.

## Future Work

- **Mount status monitoring**: query `mount/listmounts` via the RC API to show active mount status per profile
- **Unmount from UI**: call `mount/unmount` via RC API
- **Hybrid architecture**: TOML config as source of truth with embedded metadata in generated scripts (separate design doc)
