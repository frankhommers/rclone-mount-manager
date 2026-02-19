# Rclone Mount Manager v2 - Design Document

**Date:** 2026-02-19
**Status:** Approved

## Goal

Transform Rclone Mount Manager from a single-project prototype into a well-structured, distributable macOS application with typed parameter controls for all rclone mount options. Reference project: git-auto-sync.

## Current State

- Single Avalonia UI project, no solution file, no git repo
- 973-line MainWindowViewModel, 734-line MountManagerService
- Mount parameters entered as free-text string (`ExtraOptions`)
- Backend discovery via `rclone config providers` exists but only for remote creation
- No tests, no CI/CD, no README, no distribution scripts

## Target State

- Multi-project solution with Core/GUI/Tests split
- Full typed parameter controls for mount, VFS, NFS, filter, and general rclone options
- macOS .app bundle + DMG distribution
- CI/CD via GitHub Actions
- Open-source governance (README, LICENSE, CONTRIBUTING)

---

## 1. Parameter Auto-Population

### Discovery Mechanism

Use `rclone rc --loopback options/info` which returns structured JSON with all options grouped by category. Each option includes:

- `Name` - flag name (e.g., `vfs_cache_mode`)
- `Type` - data type (e.g., `bool`, `int`, `Duration`, `SizeSuffix`, `CacheMode`)
- `Default` / `DefaultStr` - default value
- `Help` - description text
- `Advanced` - whether the option is advanced
- `Groups` - optional group classification

This is called once at startup and cached. Falls back to hardcoded defaults if rclone is unavailable.

### Relevant Option Groups

| Group | Count | Purpose |
|-------|-------|---------|
| mount | 21 | FUSE/mount-specific settings |
| vfs | 32 | Virtual filesystem caching, permissions |
| nfs | 4 | NFS server settings (only for nfsmount) |
| filter | 22 | Include/exclude rules |
| main | 101 | General rclone settings (transfers, checkers, etc.) |

**Total: ~180 mount-relevant options** (backend options handled separately via existing `rclone config providers`).

### Type-to-Control Mapping

| rclone Type | UI Control | Notes |
|---|---|---|
| `bool` | ToggleSwitch | On/off |
| `int`, `int64`, `uint32`, `float64` | NumericUpDown | With sensible min/max bounds |
| `string` | TextBox | Single-line input |
| `Duration` | TextBox with validation | Accepts `5s`, `1m30s`, `1h`, etc. Tooltip shows format |
| `SizeSuffix` | TextBox with validation | Accepts `128Mi`, `1Gi`, `off`, etc. |
| `FileMode` | TextBox with validation | Octal permission string (e.g., `0777`) |
| `CacheMode` | ComboBox | Enum: `off\|minimal\|writes\|full` |
| `LogLevel` | ComboBox | Enum: `DEBUG\|INFO\|NOTICE\|ERROR` |
| `Tristate` | ComboBox | `unset\|true\|false` |
| `stringArray` | ItemsRepeater with add/remove | Multiple string entries |
| `BwTimetable` | TextBox | Advanced format, tooltip explains |
| `DumpFlags` | TextBox | Comma-separated flags |
| `SpaceSepList` | TextBox | Space-separated list |
| Pipe-delimited enums (e.g., `memory\|disk\|symlink`) | ComboBox | Auto-parsed from type string |

### UI Layout for Parameters

Each option group is a collapsible section (Expander) within a scrollable area:

```
[ Mount Options          v ]  <- Expander header with group name
  [ ] Debug FUSE                    (ToggleSwitch, default: off)
  Attr Timeout    [1s        ]      (TextBox, Duration)
  Volume Name     [          ]      (TextBox, string)
  ...
  [x] Show advanced options         (reveals Advanced=true options)

[ VFS Options            v ]
  Cache Mode      [off      v]     (ComboBox: off|minimal|writes|full)
  Dir Cache Time  [5m0s      ]     (TextBox, Duration)
  Cache Max Size  [off       ]     (TextBox, SizeSuffix)
  ...

[ NFS Options            v ]       (only visible when mount type = NFS/nfsmount)
  Cache Type      [memory   v]     (ComboBox: memory|disk|symlink)
  ...

[ Filter Options         v ]
  ...

[ General Options        v ]
  Transfers       [4     ]         (NumericUpDown)
  Checkers        [8     ]         (NumericUpDown)
  ...
```

### Value Tracking

- Each option has a `HasValue` property indicating whether the user has set it
- Only options that differ from the default are persisted and included in the mount command
- A "Reset to default" button per option clears the user's override
- Options are stored in `MountProfile` as `Dictionary<string, string>` keyed by option name

### Command Generation

When building the mount command, iterate over non-default options and emit `--{name} {value}` flags. The existing `ExtraOptions` free-text field remains as a fallback for edge cases not covered by the typed UI.

---

## 2. Project Structure Reorganization

### Solution Layout

```
rclone-mount/
├── Directory.Build.props                    # Version: 0.1.0
├── RcloneMountManager.slnx                  # Solution file
├── .gitignore
├── README.md
├── LICENSE                                  # MIT
├── CONTRIBUTING.md
├── CODE_OF_CONDUCT.md
│
├── RcloneMountManager.Core/
│   ├── RcloneMountManager.Core.csproj       # netstandard2.0 or net10.0
│   ├── Models/
│   │   ├── MountProfile.cs
│   │   ├── MountType.cs
│   │   ├── QuickConnectMode.cs
│   │   ├── RcloneBackendInfo.cs
│   │   ├── RcloneBackendOption.cs
│   │   ├── RcloneBackendOptionInput.cs
│   │   ├── RcloneOptionGroup.cs             # NEW: group of options from options/info
│   │   └── RcloneOption.cs                  # NEW: single option definition
│   └── Services/
│       ├── MountManagerService.cs
│       ├── RcloneBackendService.cs
│       ├── RcloneOptionsService.cs          # NEW: options/info discovery
│       └── LaunchAgentService.cs
│
├── RcloneMountManager.GUI/
│   ├── RcloneMountManager.GUI.csproj        # net10.0, references Core
│   ├── Program.cs
│   ├── App.axaml / App.axaml.cs
│   ├── ViewLocator.cs
│   ├── ViewModels/
│   │   ├── ViewModelBase.cs
│   │   ├── MainWindowViewModel.cs           # Slim: window state, navigation only
│   │   ├── ProfileListViewModel.cs          # Profile CRUD
│   │   ├── ProfileDetailViewModel.cs        # Selected profile editing
│   │   ├── MountOptionsViewModel.cs         # NEW: typed parameter editor
│   │   ├── BackendOptionsViewModel.cs       # Backend discovery / remote creation
│   │   └── ActivityLogViewModel.cs          # Log display
│   ├── Views/
│   │   ├── MainWindow.axaml / .cs
│   │   ├── ProfileListView.axaml / .cs      # UserControl
│   │   ├── ProfileDetailView.axaml / .cs    # UserControl
│   │   ├── MountOptionsView.axaml / .cs     # NEW: UserControl
│   │   └── BackendOptionsView.axaml / .cs   # UserControl
│   ├── Controls/
│   │   └── OptionEditorControl.axaml / .cs  # NEW: renders a single option with correct control
│   ├── Converters/
│   │   ├── OptionTypeToControlConverter.cs   # NEW
│   │   └── BoolToVisibilityConverter.cs
│   └── Assets/
│       └── avalonia-logo.ico
│
├── RcloneMountManager.Tests/
│   ├── RcloneMountManager.Tests.csproj      # net10.0, xunit
│   ├── Services/
│   │   ├── MountManagerServiceTests.cs
│   │   ├── RcloneOptionsServiceTests.cs     # NEW
│   │   └── LaunchAgentServiceTests.cs
│   └── Models/
│       └── MountProfileTests.cs
│
├── scripts/
│   ├── build-macos-app.sh
│   └── create-dmg.sh
│
├── .github/
│   ├── workflows/
│   │   └── release-dmg.yml
│   ├── ISSUE_TEMPLATE/
│   │   ├── bug_report.md
│   │   └── feature_request.md
│   └── pull_request_template.md
│
└── docs/
    └── plans/
        └── 2026-02-19-next-level-design.md  # This document
```

### Key Decisions

- **Core project** contains all models and services (no UI dependency). This enables testing without Avalonia.
- **GUI project** references Core, contains ViewModels, Views, and custom controls.
- **Tests project** references Core directly, uses xunit.
- **net10.0** target for all projects (matching current setup).

---

## 3. ViewModel Refactoring

### Current: 1 ViewModel, ~973 lines

`MainWindowViewModel` currently handles:
- Profile list management
- Profile detail editing
- Mount/unmount operations
- Backend discovery
- Script generation
- Activity logging
- Theme management

### Target: 6 ViewModels

| ViewModel | Responsibility | Estimated size |
|---|---|---|
| `MainWindowViewModel` | Window state, theme, navigation between child VMs | ~100 lines |
| `ProfileListViewModel` | Profile CRUD, selection, persistence | ~150 lines |
| `ProfileDetailViewModel` | Edit selected profile fields, save/test/mount/unmount | ~250 lines |
| `MountOptionsViewModel` | Typed parameter editor, option groups, advanced toggle | ~200 lines |
| `BackendOptionsViewModel` | Backend discovery, remote creation | ~150 lines |
| `ActivityLogViewModel` | Log entries, status bar | ~50 lines |

Communication between ViewModels via:
- Direct property binding (parent passes selected profile to children)
- Event callbacks for cross-cutting actions (e.g., log messages)

---

## 4. MountProfile Model Changes

### Current fields (unchanged)
- `Name`, `Type`, `Source`, `MountPoint`, `ExtraOptions`
- `RcloneBinaryPath`, `QuickConnect*` fields
- `AllowInsecurePasswordsInScript`

### New fields
```csharp
// Typed mount options - only stores non-default values
public Dictionary<string, string> MountOptions { get; set; } = new();

// Tracks which option groups the user has customized (for UI state)
public HashSet<string> ExpandedOptionGroups { get; set; } = new();
```

### Backward Compatibility

Existing profiles with `ExtraOptions` continue to work. The free-text field remains available as an "Additional options" field below the typed controls. During mount command generation, typed options are emitted first, followed by any extra options.

---

## 5. macOS Distribution

### App Bundle (build-macos-app.sh)
- Creates `RcloneMountManager.app` with proper `Info.plist`
- `dotnet publish` with `-r osx-arm64` (and optionally `osx-x64`)
- Self-contained single-file publish
- Code signing placeholder (for future Apple Developer ID)

### DMG (create-dmg.sh)
- Creates `.dmg` with app + Applications symlink
- Background image, icon positioning
- SHA256 checksum generation

### GitHub Actions (release-dmg.yml)
- Trigger on tag push (`v*`)
- Matrix build: arm64 + x64
- Upload DMG as release asset with checksums

---

## 6. Testing Strategy

### Unit Tests (xunit)

- `RcloneOptionsServiceTests` - Parse known JSON, verify option groups and types
- `MountManagerServiceTests` - Command building with typed options, argument escaping
- `MountProfileTests` - Serialization, backward compatibility with old profiles
- `LaunchAgentServiceTests` - Plist generation, script generation

### Integration Tests (conditional)

- Skip if rclone not installed (`[SkipIfNoRclone]` attribute)
- Verify `rclone rc --loopback options/info` parsing against live rclone
- Verify mount command syntax against `rclone mount --help`

---

## 7. Open-Source Governance

- **README.md** - Project description, screenshots, installation, usage, building from source
- **LICENSE** - MIT
- **CONTRIBUTING.md** - How to contribute, development setup, code style
- **CODE_OF_CONDUCT.md** - Contributor Covenant
- **Issue templates** - Bug report, feature request
- **PR template** - Checklist for reviewers

---

## Implementation Priority

1. **Git init + project restructure** - Foundation for everything else
2. **RcloneOptionsService + models** - Core discovery mechanism
3. **MountOptionsViewModel + UI** - The main new feature
4. **ViewModel refactoring** - Split MainWindowViewModel
5. **MountProfile model update** - Typed options storage
6. **Command generation update** - Use typed options in mount commands
7. **Tests** - Cover new and existing services
8. **Distribution scripts** - macOS app bundle + DMG
9. **CI/CD** - GitHub Actions
10. **Governance docs** - README, LICENSE, etc.
