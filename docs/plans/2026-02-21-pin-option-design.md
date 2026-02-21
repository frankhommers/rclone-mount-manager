# Pin Option Design

## Problem

When a user sets an option to its default value (e.g. `--vfs-cache-mode off` when default is also `off`), it gets filtered out by `HasNonDefaultValue` and doesn't appear in the generated script or command line. Sometimes users want to explicitly include a default value.

## Design

### Concept: Pin Button

Each option gets a small pin icon button. When pinned, the option's value is always included in the script/command output, even if it equals the default.

### Behavior

- **User changes value to non-default** -> pin turns on automatically
- **User clicks Reset to Default** -> pin turns off
- **User manually pins** -> value is included even if it equals default
- **User manually unpins** -> value is only included if non-default (current behavior)

### Property: `IsPinned`

Added to `TypedOptionViewModel` (base class), so it works for both mount options and backend options, and for all control types (Toggle, ComboBox, Numeric, Duration, SizeSuffix, Text, StringList).

### Property: `ShouldInclude`

Replaces `HasNonDefaultValue` as the decision for whether to include an option in output:

```
ShouldInclude = IsPinned || HasNonDefaultValue
```

`HasNonDefaultValue` remains for visual styling (bold label, accent border).

### Auto-pin logic

In `SyncToString()` and `OnValueChangedExtra()`: if the new value differs from the normalized default, set `IsPinned = true`.

In `ResetToDefault()`: set `IsPinned = false`.

### UI

- Pin icon button appears next to the reset button in all 7 DataTemplates
- Uses a pushpin PathIcon
- When pinned: icon uses accent color / filled appearance
- When unpinned: icon is subtle/transparent
- Tooltip: "Pin to always include in script" / "Pinned - will be included in script"

### Data flow

- `MountOptionsViewModel.GetNonDefaultValues()` renamed or adjusted to use `ShouldInclude` instead of `HasNonDefaultValue`
- `MountOptionInputViewModel.HasNonDefaultValue` override: `IsSet` requirement stays for visual styling
- New `ShouldInclude`: `IsPinned || HasNonDefaultValue`
- `GenerateScript` and `StartMount` use `ShouldInclude` via the collected options dictionary

### Persistence

`MountProfile.MountOptions` dictionary already stores all set options. The sync now uses `ShouldInclude` to decide what goes in. Pinned default values will be persisted.

### Backend options

`RcloneBackendOptionInput` inherits `IsPinned` from `TypedOptionViewModel`. The `CreateRemoteAsync` flow in `RcloneBackendService` should also respect `ShouldInclude`.

## Scope

- All 7 option control types
- Both mount options and backend options
- Script generation, mount start, save script, toggle startup
