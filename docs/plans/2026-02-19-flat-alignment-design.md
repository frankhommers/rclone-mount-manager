# Flat Alignment Design

## Problem

The UI has 3+ alignment contexts with different nesting depths (Borders, Expanders, Padding, BorderThickness) causing labels and controls to start at different x-positions. It looks unprofessional — "een speeltuin van controls op random plaatsen."

## Goal

macOS System Preferences style: flat, clean, no decorative borders around form fields. All labels on the same vertical line. Section headers + separators for visual grouping.

## Design

### Layout rule

One fixed label column of 200px measured from the left edge of the content StackPanel. No element may add padding, border, or margin that shifts this.

### Structure

```
ScrollViewer > StackPanel Spacing="10" Margin="20,14"

Section: "Backend Configuration"          [Reload types button]
  Helper text
  Grid 200,*  Backend type      | ComboBox (stretch)
  Helper text (description)
  Grid 200,*  Remote name       | TextBox (stretch)
  CheckBox    Show advanced backend options (conditional)
  ItemsControl backend options (flat rows, no wrapper)
  Helper text "No options" (conditional)
  Button      Create remote

Separator

Section: "Profile Settings"
  Grid 200,*  Profile name      | TextBox
  Grid 200,*  Mount type        | ComboBox
  Grid 200,*  Source            | TextBox
  Helper text
  Grid 200,*  Local mount path  | TextBox
  Grid 200,*  Rclone binary     | TextBox
  CheckBox    UNSECURE passwords

Separator

Section: "Mount Parameters"
  Expander "Mount" (no wrapper Border, no extra padding)
    Option rows (flat Grid 200,*,Auto with Margin="0,2")
  Expander "VFS Cache"
    Option rows
  ...

Separator

Actions: Save | Test | Start | Stop | Generate | Save script | Startup

Separator

Section: "Script preview"
  TextBox (monospace)

Section: "Activity"
  ListBox (monospace)
```

### Option template changes (App.axaml)

Current: Each option is `Border Padding="6,5" BorderThickness="3,0,0,0"` containing `Grid ColumnDefinitions="200,*,Auto"`.

New: Each option is a bare `Grid ColumnDefinitions="200,*,Auto" Margin="0,2"`. No Border wrapper.

The 3px left accent border for modified values is removed. Modified values are indicated by:
- Bold label (already exists via BoolToFontWeightConverter)
- Reset button appears (already exists)

### MountOptionsView changes

- Remove wrapper `Border` from MainWindow around MountOptionsView
- Expanders have no extra padding, so option rows inside align with form fields outside

### Files changed

1. `App.axaml` — 6 DataTemplates: remove Border wrapper, bare Grid rows
2. `MainWindow.axaml` — remove all decorative Borders, flat layout with Separators
3. `MountOptionsView.axaml` — remove "Show advanced options" checkbox (useless, set default true), ensure Expanders have no padding offset

### What stays

- AccentBorderConverter and BoolToFontWeightConverter (font weight still used)
- OptionControlTemplateSelector and factory (unchanged)
- All ViewModel logic (unchanged)
- All 67 tests (unchanged)

### Also included (from stashed changes)

- Remove useless "Show advanced options" checkbox (rclone reports 0 advanced mount options)
- ShowAdvancedOptions defaults to true
- Remove Advanced Expander (Extra options redundant, rclone binary path + insecure checkbox inline)
- Column widths standardized to 200px everywhere
