# Flat Alignment Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Rewrite the UI layout to use flat alignment with no nesting offsets — macOS System Preferences style.

**Architecture:** Remove all decorative Border wrappers from form sections. Replace with section headers + Separators. Remove Border wrappers from option DataTemplates. All label-value pairs use `Grid ColumnDefinitions="200,*"` directly in the content StackPanel — no intermediate containers that add padding.

**Tech Stack:** Avalonia UI 11.3, .NET 10, XAML

---

### Task 1: Flatten option DataTemplates in App.axaml

**Files:**
- Modify: `RcloneMountManager.GUI/App.axaml`

**What to change:**

All 6 DataTemplates (Toggle, ComboBox, Numeric, Duration, SizeSuffix, Text) currently wrap content in:
```xml
<Border Padding="8,6" Margin="0,0,0,1" BorderThickness="3,0,0,0" CornerRadius="4"
        BorderBrush="{Binding HasNonDefaultValue, Converter={StaticResource AccentBorderConverter}}">
    <Grid ColumnDefinitions="220,*,Auto" RowDefinitions="Auto,Auto">
```

Replace with a bare Grid — no Border:
```xml
<Grid ColumnDefinitions="200,*,Auto" RowDefinitions="Auto,Auto" Margin="0,2">
```

For each template:
1. Remove the `<Border>` open and close tags
2. Change `ColumnDefinitions` from `"220,*,Auto"` to `"200,*,Auto"`
3. Add `Margin="0,2"` to the Grid
4. Add `TextTrimming="CharacterEllipsis"` to label TextBlocks
5. Keep all control bindings, reset buttons, help text TextBlocks exactly as-is

The `AccentBorderConverter` resource can stay in App.axaml — it's no longer used by templates but removing it would break compilation if referenced elsewhere. (Or verify it's unused and remove.)

**Verify:** `dotnet build` — 0 warnings 0 errors.

**Commit:** `refactor: flatten option DataTemplates, remove Border wrappers for alignment`

---

### Task 2: Rewrite MainWindow.axaml to flat layout

**Files:**
- Modify: `RcloneMountManager.GUI/Views/MainWindow.axaml`

**What to change:**

Replace the entire content of `<ScrollViewer Grid.Row="1">` (currently lines 125-265) with a flat StackPanel layout. The key rules:

1. NO `<Border>` wrappers around form sections
2. ALL `Grid ColumnDefinitions` use `"200,*"` (matching the templates' 200px label column)
3. Section headers: `<TextBlock FontWeight="SemiBold" FontSize="14"/>`
4. Sections separated by `<Separator Margin="0,4"/>`
5. Helper text: `FontSize="11" Opacity="0.6" Margin="0,-6,0,0"` (negative top margin pulls it closer to the field above)
6. The MountOptionsView is placed directly — NO wrapper Border

The exact layout order:

```
StackPanel Spacing="10" Margin="20,14"
  -- Backend Configuration --
  Header row (TextBlock + Reload button)
  Helper text
  Grid: Backend type | ComboBox
  Helper text (description)
  Grid: Remote name | TextBox
  CheckBox: Show advanced backend options (conditional)
  ItemsControl: backend options
  TextBlock: "No options" (conditional)
  Button: Create remote

  Separator

  -- Profile Settings --
  Header
  Grid: Profile name | TextBox
  Grid: Mount type | ComboBox
  Grid: Source | TextBox
  Helper text
  Grid: Local mount path | TextBox
  Grid: Rclone binary path | TextBox
  CheckBox: UNSECURE passwords

  Separator

  -- Mount Parameters --
  MountOptionsView (NO Border wrapper)

  Separator

  -- Actions --
  WrapPanel with buttons

  Separator

  -- Script preview --
  Header
  TextBox (monospace)

  -- Activity --
  Header
  ListBox (monospace)
```

Also:
- Remove the Advanced Expander entirely (was: Extra options, Rclone binary path, insecure checkbox)
- Move Rclone binary path to Profile Settings section
- Move insecure checkbox to Profile Settings section
- Remove Extra options field (redundant with typed Mount Parameters)

**Verify:** `dotnet build` — 0 warnings 0 errors.

**Commit:** `refactor: rewrite MainWindow to flat layout with consistent 200px label column`

---

### Task 3: Simplify MountOptionsView

**Files:**
- Modify: `RcloneMountManager.GUI/Views/MountOptionsView.axaml`
- Modify: `RcloneMountManager.GUI/ViewModels/MountOptionsViewModel.cs`

**What to change in MountOptionsView.axaml:**

1. Remove the "Show advanced options" CheckBox (rclone reports 0 advanced mount options — it's dead UI)
2. Section header is just `<TextBlock Text="Mount Parameters" FontWeight="SemiBold" FontSize="14"/>`
3. Expanders should NOT add extra padding. Ensure `Margin="0"` on Expander content

**What to change in MountOptionsViewModel.cs:**

1. Change `private bool _showAdvancedOptions;` default to `true`:
   ```csharp
   private bool _showAdvancedOptions = true;
   ```

**Verify:** `dotnet build` — 0 warnings 0 errors. `dotnet test` — 67 passed.

**Commit:** `refactor: simplify MountOptionsView, remove dead advanced checkbox`

---

### Task 4: Final verification

**Verify:**
- `dotnet build` — 0 warnings 0 errors
- `dotnet test` — 67 passed 0 failed
- `git status` — clean working tree (only .idea/ untracked)
- `git log --oneline -5` — show recent commits
