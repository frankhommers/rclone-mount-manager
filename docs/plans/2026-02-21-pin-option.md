# Pin Option Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Allow users to pin an option so its value is always included in scripts and commands, even when it equals the default.

**Architecture:** Add `IsPinned` property to `TypedOptionViewModel` (base class). Auto-pin when value differs from default. Add `ShouldInclude` property that combines pin state with non-default check. Add pin button to all 7 XAML templates. Update `GetNonDefaultValues` to use `ShouldInclude`.

**Tech Stack:** C# / MVVM Toolkit / Avalonia 11.3

---

### Task 1: Add IsPinned and ShouldInclude to TypedOptionViewModel

**Files:**
- Modify: `RcloneMountManager.Core/ViewModels/TypedOptionViewModel.cs`
- Test: `RcloneMountManager.Tests/ViewModels/MountOptionInputViewModelTests.cs`

**Step 1: Write failing tests**

Add these tests to `MountOptionInputViewModelTests.cs`:

```csharp
[Fact]
public void IsPinned_DefaultFalse()
{
    var option = new RcloneOption { Name = "transfers", Type = "int" };
    var vm = new MountOptionInputViewModel(option);
    Assert.False(vm.IsPinned);
}

[Fact]
public void IsPinned_AutoPinsOnNonDefaultValue()
{
    var option = new RcloneOption { Name = "transfers", Type = "int", DefaultStr = "4" };
    var vm = new MountOptionInputViewModel(option);
    vm.NumericValue = 8;
    Assert.True(vm.IsPinned);
}

[Fact]
public void IsPinned_ResetUnpins()
{
    var option = new RcloneOption { Name = "transfers", Type = "int", DefaultStr = "4" };
    var vm = new MountOptionInputViewModel(option);
    vm.NumericValue = 8;
    Assert.True(vm.IsPinned);
    vm.ResetToDefaultCommand.Execute(null);
    Assert.False(vm.IsPinned);
}

[Fact]
public void ShouldInclude_FalseByDefault()
{
    var option = new RcloneOption { Name = "transfers", Type = "int" };
    var vm = new MountOptionInputViewModel(option);
    Assert.False(vm.ShouldInclude);
}

[Fact]
public void ShouldInclude_TrueWhenNonDefault()
{
    var option = new RcloneOption { Name = "transfers", Type = "int", DefaultStr = "4" };
    var vm = new MountOptionInputViewModel(option);
    vm.NumericValue = 8;
    Assert.True(vm.ShouldInclude);
}

[Fact]
public void ShouldInclude_TrueWhenPinnedAtDefault()
{
    var option = new RcloneOption { Name = "vfs_cache_mode", Type = "string", DefaultStr = "off" };
    var vm = new MountOptionInputViewModel(option);
    vm.Value = "off";
    // Value equals default, so HasNonDefaultValue is false
    // But manually pin it
    vm.IsPinned = true;
    Assert.True(vm.ShouldInclude);
}

[Fact]
public void ShouldInclude_FalseWhenUnpinnedAtDefault()
{
    var option = new RcloneOption { Name = "vfs_cache_mode", Type = "string", DefaultStr = "off" };
    var vm = new MountOptionInputViewModel(option);
    vm.Value = "off";
    vm.IsPinned = false;
    Assert.False(vm.ShouldInclude);
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "IsPinned|ShouldInclude"`
Expected: FAIL — `IsPinned` and `ShouldInclude` do not exist yet.

**Step 3: Add IsPinned and ShouldInclude to TypedOptionViewModel**

In `TypedOptionViewModel.cs`, add after the `_selectedEnumValue` field (line 50):

```csharp
[ObservableProperty]
private bool _isPinned;
```

Add after `HasNonDefaultValue` (line 54):

```csharp
public virtual bool ShouldInclude =>
    IsPinned || HasNonDefaultValue;
```

In `SyncToString()` (line 172), add auto-pin logic after setting Value:

```csharp
protected virtual void SyncToString(string newValue)
{
    _syncing = true;
    try
    {
        Value = newValue;
        OnPropertyChanged(nameof(HasNonDefaultValue));
        if (!string.IsNullOrEmpty(newValue) &&
            !string.Equals(newValue, NormalizedDefaultStr, StringComparison.OrdinalIgnoreCase))
        {
            IsPinned = true;
        }
        OnPropertyChanged(nameof(ShouldInclude));
    }
    finally
    {
        _syncing = false;
    }
}
```

In `OnValueChanged()` (line 191), add PropertyChanged for ShouldInclude:

```csharp
OnPropertyChanged(nameof(ShouldInclude));
```

In `ResetToDefault()` (line 285), add unpin:

```csharp
[RelayCommand]
protected virtual void ResetToDefault()
{
    IsPinned = false;
    Value = string.Empty;
    OnPropertyChanged(nameof(HasNonDefaultValue));
    OnPropertyChanged(nameof(ShouldInclude));
}
```

**Step 4: Update MountOptionInputViewModel overrides**

In `MountOptionInputViewModel.cs`, add ShouldInclude override:

```csharp
public override bool ShouldInclude =>
    (IsPinned && IsSet) || HasNonDefaultValue;
```

Update `SyncToString` override to auto-pin and auto-set IsSet:

```csharp
protected override void SyncToString(string newValue)
{
    base.SyncToString(newValue);
    if (!string.IsNullOrEmpty(newValue) &&
        !string.Equals(newValue, NormalizedDefaultStr, StringComparison.OrdinalIgnoreCase))
    {
        IsSet = true;
        IsPinned = true;
    }
    OnPropertyChanged(nameof(ShouldInclude));
}
```

Update `ResetToDefault`:

```csharp
protected override void ResetToDefault()
{
    base.ResetToDefault();
    IsSet = false;
}
```

Update `OnValueChangedExtra`:

```csharp
protected override void OnValueChangedExtra(string value)
{
    if (!string.IsNullOrEmpty(value) &&
        !string.Equals(value, NormalizedDefaultStr, StringComparison.OrdinalIgnoreCase))
    {
        IsSet = true;
        IsPinned = true;
    }
    OnPropertyChanged(nameof(ShouldInclude));
}
```

Add `OnIsPinnedChanged`:

```csharp
partial void OnIsPinnedChanged(bool value)
{
    OnPropertyChanged(nameof(ShouldInclude));
}
```

**Step 5: Run tests to verify they pass**

Run: `dotnet test`
Expected: All tests pass.

**Step 6: Commit**

```
feat: add IsPinned and ShouldInclude properties to option ViewModels
```

---

### Task 2: Update GetNonDefaultValues to use ShouldInclude

**Files:**
- Modify: `RcloneMountManager.GUI/ViewModels/MountOptionsViewModel.cs:46-60`
- Modify: `RcloneMountManager.GUI/ViewModels/MountOptionsViewModel.cs:62-83`

**Step 1: Update GetNonDefaultValues**

Rename is not needed — just change the filter from `HasNonDefaultValue` to `ShouldInclude`:

```csharp
public Dictionary<string, string> GetNonDefaultValues()
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var group in Groups)
    {
        foreach (var option in group.AllOptions)
        {
            if (option.ShouldInclude)
            {
                result[option.Name] = option.Value;
            }
        }
    }
    return result;
}
```

Also update `ToCommandLineArguments` to use `ShouldInclude`:

```csharp
public IReadOnlyList<string> ToCommandLineArguments()
{
    var args = new List<string>();
    foreach (var kvp in GetNonDefaultValues())
    {
        // ... (unchanged logic)
    }
    return args;
}
```

(No change needed in `ToCommandLineArguments` since it calls `GetNonDefaultValues` which already uses `ShouldInclude`.)

**Step 2: Update MountOptionGroupViewModel header/counters**

In `MountOptionGroupViewModel`, update `ModifiedCount` and `Header` to reflect pinned options:

```csharp
public int ModifiedCount => VisibleOptions.Count(o => o.ShouldInclude);
public bool HasModifiedOptions => ModifiedCount > 0;
public string Header => $"{DisplayName} ({VisibleOptions.Count(o => o.ShouldInclude)}/{VisibleOptions.Count()})";
```

**Step 3: Run tests and build**

Run: `dotnet build && dotnet test`
Expected: 0 warnings, 0 errors, all tests pass.

**Step 4: Commit**

```
feat: use ShouldInclude instead of HasNonDefaultValue for option collection
```

---

### Task 3: Add pin button to all 7 XAML templates

**Files:**
- Modify: `RcloneMountManager.GUI/App.axaml`

**Step 1: Add pin button to each template**

For each of the 7 templates (Toggle, ComboBox, Numeric, Duration, SizeSuffix, Text, StringList), change `ColumnDefinitions` from `200,*,Auto` to `200,*,Auto,Auto` and add a pin button in Grid.Column="2", shifting the reset button to Grid.Column="3". Also shift the help text to `Grid.ColumnSpan="4"`.

Pin button pattern (same for all templates):

```xml
<Button Grid.Row="0" Grid.Column="2"
        Command="{Binding TogglePinCommand}"
        Padding="6,4" Margin="0,0,0,0"
        ToolTip.Tip="Pin to always include in script">
    <PathIcon Data="M16 12V4h1V2H7v2h1v8l-2 2v2h5.2v6h1.6v-6H18v-2l-2-2z"
              Width="12" Height="12"
              Opacity="{Binding IsPinned, Converter={StaticResource PinOpacityConverter}}"/>
</Button>
```

Reset button shifts to Grid.Column="3".

**Step 2: Add TogglePinCommand to TypedOptionViewModel**

```csharp
[RelayCommand]
private void TogglePin()
{
    IsPinned = !IsPinned;
    OnPropertyChanged(nameof(ShouldInclude));
}
```

**Step 3: Add PinOpacityConverter**

Create `RcloneMountManager.GUI/Converters/BoolToOpacityConverter.cs`:

```csharp
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace RcloneMountManager.Converters;

public class BoolToOpacityConverter : IValueConverter
{
    public double TrueOpacity { get; set; } = 1.0;
    public double FalseOpacity { get; set; } = 0.3;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? TrueOpacity : FalseOpacity;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

Register in `App.axaml` resources:

```xml
<converters:BoolToOpacityConverter x:Key="PinOpacityConverter"
                                    TrueOpacity="1.0"
                                    FalseOpacity="0.3"/>
```

**Step 4: Build and verify**

Run: `dotnet build`
Expected: 0 warnings, 0 errors.

**Step 5: Commit**

```
feat: add pin button to all option templates
```

---

### Task 4: Update visual indicators for pinned state

**Files:**
- Modify: `RcloneMountManager.GUI/App.axaml`

**Step 1: Update bold label and accent border to also trigger on IsPinned**

The current `FontWeight` binding uses `HasNonDefaultValue`. Change it to use `ShouldInclude` so pinned-at-default options also get the visual indicator:

In all 7 templates, change:
```xml
FontWeight="{Binding HasNonDefaultValue, Converter={StaticResource ModifiedFontWeightConverter}}"
```
to:
```xml
FontWeight="{Binding ShouldInclude, Converter={StaticResource ModifiedFontWeightConverter}}"
```

Also update the reset button visibility to show when `ShouldInclude`:
```xml
IsVisible="{Binding ShouldInclude}"
```

**Step 2: Build and test**

Run: `dotnet build && dotnet test`
Expected: 0 warnings, 0 errors, all tests pass.

**Step 3: Commit**

```
feat: update visual indicators to reflect pinned state
```

---

### Task 5: Final verification

**Step 1: Run full build and test suite**

Run: `dotnet build && dotnet test`
Expected: 0 warnings, 0 errors, all tests pass.

**Step 2: Manual verification checklist**

- [ ] Pin button appears on all option types
- [ ] Changing a value auto-pins
- [ ] Reset unpins
- [ ] Manual pin at default value → option appears in generated script
- [ ] Manual unpin → option disappears from script if at default
- [ ] Pin icon opacity changes between pinned/unpinned
- [ ] Bold label appears for pinned options
