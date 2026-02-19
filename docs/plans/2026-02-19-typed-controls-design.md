# Typed Parameter Controls & UI Polish

**Date:** 2026-02-19
**Status:** Approved

## Problem

All mount parameters currently render as plain TextBox controls, regardless of their actual type. Boolean parameters require typing "true"/"false" as text, enums have no dropdown, numeric values have no spinners, and durations have no structured input. This is confusing (especially the true/false meaning for booleans) and error-prone.

## Goals

1. **Type-specific controls** — Render the right UI control for each parameter type
2. **Visual polish** — Make the parameter editor look professional and polished
3. **Inline validation** — Prevent invalid input through control constraints
4. **Maximum beauty** — Best possible visual quality using Avalonia Fluent theme capabilities

## Architecture: DataTemplateSelector

Use Avalonia's `IDataTemplate` pattern to create an `OptionControlTemplateSelector` that selects the correct DataTemplate based on `MountOptionInputViewModel.ControlType`.

```
OptionControlTemplateSelector : IDataTemplate
├── ToggleTemplate      → ToggleSwitch (bool)
├── ComboBoxTemplate    → ComboBox (CacheMode, Tristate, LogLevel, pipe-delimited enums)
├── NumericTemplate     → NumericUpDown (int, int64, uint32, float64)
├── DurationTemplate    → TimePicker (Duration — hours/minutes/seconds)
├── SizeSuffixTemplate  → NumericUpDown + ComboBox unit selector (SizeSuffix)
└── TextTemplate        → TextBox with watermark (string, stringArray, etc.)
```

## Control Specifications

### Toggle (bool parameters)

- **Control:** `ToggleSwitch` with OnContent="Enabled" / OffContent="Disabled"
- **Binding:** New `BoolValue` (bool) property on ViewModel, syncs to `Value` as "true"/"false"
- **Visual:** Clear on/off state, no more ambiguous text input
- **Examples:** `--debug-fuse`, `--no-check-certificate`, `--allow-other`

### ComboBox (enum parameters)

- **Control:** `ComboBox` bound to `EnumValues` collection
- **Binding:** New `SelectedEnumValue` (string?) property, syncs to `Value`
- **Placeholder:** Watermark shows default value when nothing selected
- **Examples:** `--vfs-cache-mode` (off/minimal/writes/full), `--log-level` (DEBUG/INFO/NOTICE/ERROR)

### NumericUpDown (numeric parameters)

- **Control:** `NumericUpDown` with spinner buttons
- **Binding:** New `NumericValue` (decimal?) property, syncs to `Value`
- **Constraints:** Minimum=0 for unsigned types, appropriate increment (1 for int, 0.1 for float64)
- **Watermark:** Shows default value
- **Examples:** `--buffer-size`, `--checkers`, `--transfers`

### TimePicker (Duration parameters)

- **Control:** Avalonia `TimePicker` with `UseSeconds="True"` and `ClockIdentifier="24HourClock"`
- **Binding:** New `DurationValue` (TimeSpan?) property, converts to/from rclone duration format (e.g., "5m30s", "1h", "10s")
- **Parsing:** Parse rclone durations to TimeSpan, format back on save
- **Examples:** `--dir-cache-time`, `--vfs-write-back`, `--poll-interval`
- **Note:** TimePicker maxes at 23:59:59. For durations > 24h, fall back to TextBox with validation.

### SizeSuffix (size parameters)

- **Control:** `NumericUpDown` (value) + `ComboBox` (unit: B, Ki, Mi, Gi, Ti)
- **Binding:** New `SizeSuffixNumericValue` (decimal?) and `SizeSuffixUnit` (string) properties
- **Parsing:** Parse rclone suffixes (e.g., "128Mi" → 128 + "Mi"), compose back on save
- **Minimum:** 0 (no negative sizes)
- **Examples:** `--buffer-size`, `--vfs-cache-max-size`, `--multi-thread-chunk-size`

### Text (string, stringArray, etc.)

- **Control:** `TextBox` with `Watermark` showing default
- **No change** from current behavior, already correct for free-form text
- **Validation:** Red border for empty required fields via `DataValidationErrors`

## Visual Polish

### Modified value indication
- Parameters with non-default values get a **left accent border** (4px, theme accent color)
- Label text becomes **SemiBold** when value differs from default

### Reset button
- Replace "x" text with a **PathIcon** (undo/reset arrow icon)
- Slightly larger touch target with hover highlight
- ToolTip: "Reset to default (value)"

### Help text
- Keep visible below the control (user preference)
- Slightly improved typography: 11px, 0.5 opacity, italic style
- For long help texts: max 2 lines with TextTrimming, full text in ToolTip

### Group headers
- Display format: "Group Name" with a **Badge** showing modified count
- Badge uses accent color when count > 0, subtle/muted when 0
- Smooth expand/collapse animation (Avalonia Expander default)

### Spacing and layout
- Consistent 8px vertical spacing between parameter rows
- Separator line between parameters (subtle, 0.15 opacity)
- Group content padding: 12px
- Control column minimum width: 200px for comfortable interaction

### Overall theme integration
- Use `DynamicResource` theme brushes throughout
- Respect light/dark theme switching
- Fluent design acrylic/reveal effects where appropriate

## ViewModel Changes

### MountOptionInputViewModel additions

```csharp
// Typed value properties (sync bidirectionally with Value string)
public bool BoolValue { get; set; }              // Toggle
public decimal? NumericValue { get; set; }       // NumericUpDown
public TimeSpan? DurationValue { get; set; }     // TimePicker
public decimal? SizeSuffixNumericValue { get; set; }  // SizeSuffix number part
public string SizeSuffixUnit { get; set; }       // SizeSuffix unit part
public string? SelectedEnumValue { get; set; }   // ComboBox

// Available units for SizeSuffix
public static IReadOnlyList<string> SizeSuffixUnits => ["B", "Ki", "Mi", "Gi", "Ti"];

// Validation
public bool HasValidationError { get; }
public string? ValidationMessage { get; }
```

### Initialization flow

When `MountOptionInputViewModel` is constructed:
1. Determine `ControlType` from `RcloneOption`
2. Parse `currentValue` string into the appropriate typed property
3. Changes to typed properties sync back to `Value` string
4. Changes to `Value` string (e.g., from reset) sync to typed properties

## Files to Create/Modify

### New files
- `RcloneMountManager.GUI/Controls/OptionControlTemplateSelector.cs`

### Modified files
- `RcloneMountManager.GUI/ViewModels/MountOptionInputViewModel.cs` — Add typed properties
- `RcloneMountManager.GUI/Views/MountOptionsView.axaml` — Replace single DataTemplate with selector + 6 templates
- `RcloneMountManager.Tests/Models/RcloneOptionTests.cs` — Add tests for value parsing/formatting

## Command Generation Impact

No changes needed to `MountManagerService.cs` or `MountOptionsViewModel.ToCommandLineArguments()`. The typed properties all sync back to the `Value` string property, which is what these methods already consume. The "true"/"false" string convention continues to work — it's just now set by a ToggleSwitch instead of manual text entry.
