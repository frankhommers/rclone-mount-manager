# Typed Parameter Controls & UI Polish — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace all plain TextBox parameter controls with type-appropriate controls (ToggleSwitch, ComboBox, NumericUpDown, TimePicker, SizeSuffix combo) and add visual polish including accent borders, better icons, and validation.

**Architecture:** DataTemplateSelector pattern — an `OptionControlTemplateSelector` class selects the correct DataTemplate per `ControlType`. The `MountOptionInputViewModel` gains typed value properties (BoolValue, NumericValue, DurationValue, etc.) that bidirectionally sync with the existing `Value` string property. No changes to command generation or persistence.

**Tech Stack:** Avalonia UI 11.3, CommunityToolkit.Mvvm 8.4, .NET 10, xUnit

---

### Task 1: Add Duration parsing/formatting helpers to Core

**Files:**
- Create: `RcloneMountManager.Core/Helpers/DurationHelper.cs`
- Create: `RcloneMountManager.Tests/Helpers/DurationHelperTests.cs`

**Step 1: Write the failing tests**

In `RcloneMountManager.Tests/Helpers/DurationHelperTests.cs`:

```csharp
using RcloneMountManager.Core.Helpers;

namespace RcloneMountManager.Tests.Helpers;

public class DurationHelperTests
{
    [Theory]
    [InlineData("5m", 0, 5, 0)]
    [InlineData("1h", 1, 0, 0)]
    [InlineData("1h30m", 1, 30, 0)]
    [InlineData("10s", 0, 0, 10)]
    [InlineData("2h5m30s", 2, 5, 30)]
    [InlineData("90s", 0, 1, 30)]
    [InlineData("0", 0, 0, 0)]
    [InlineData("0s", 0, 0, 0)]
    [InlineData("", 0, 0, 0)]
    public void Parse_ValidDuration_ReturnsTimeSpan(string input, int hours, int minutes, int seconds)
    {
        var result = DurationHelper.Parse(input);
        Assert.Equal(new TimeSpan(hours, minutes, seconds), result);
    }

    [Theory]
    [InlineData(0, 5, 0, "5m")]
    [InlineData(1, 0, 0, "1h")]
    [InlineData(1, 30, 0, "1h30m")]
    [InlineData(0, 0, 10, "10s")]
    [InlineData(2, 5, 30, "2h5m30s")]
    [InlineData(0, 0, 0, "0s")]
    public void Format_TimeSpan_ReturnsRcloneString(int hours, int minutes, int seconds, string expected)
    {
        var ts = new TimeSpan(hours, minutes, seconds);
        Assert.Equal(expected, DurationHelper.Format(ts));
    }

    [Fact]
    public void Parse_Null_ReturnsZero()
    {
        Assert.Equal(TimeSpan.Zero, DurationHelper.Parse(null));
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~DurationHelperTests" -v minimal`
Expected: FAIL — `DurationHelper` class does not exist

**Step 3: Write implementation**

In `RcloneMountManager.Core/Helpers/DurationHelper.cs`:

```csharp
using System;
using System.Text.RegularExpressions;

namespace RcloneMountManager.Core.Helpers;

public static partial class DurationHelper
{
    [GeneratedRegex(@"(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?", RegexOptions.Compiled)]
    private static partial Regex DurationRegex();

    public static TimeSpan Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input == "0")
            return TimeSpan.Zero;

        // Try pure seconds first (e.g., "90s" or just "90")
        if (long.TryParse(input.TrimEnd('s'), out var totalSeconds) && !input.Contains('h') && !input.Contains('m'))
            return TimeSpan.FromSeconds(totalSeconds);

        var match = DurationRegex().Match(input);
        if (!match.Success || match.Length == 0)
            return TimeSpan.Zero;

        int hours = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
        int minutes = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
        int seconds = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;

        return new TimeSpan(hours, minutes, seconds);
    }

    public static string Format(TimeSpan ts)
    {
        if (ts <= TimeSpan.Zero) return "0s";

        var parts = new System.Text.StringBuilder();
        if (ts.Hours > 0) parts.Append($"{ts.Hours}h");
        if (ts.Minutes > 0) parts.Append($"{ts.Minutes}m");
        if (ts.Seconds > 0 || parts.Length == 0) parts.Append($"{ts.Seconds}s");

        return parts.ToString();
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~DurationHelperTests" -v minimal`
Expected: All PASS

**Step 5: Commit**

```bash
git add RcloneMountManager.Core/Helpers/DurationHelper.cs RcloneMountManager.Tests/Helpers/DurationHelperTests.cs
git commit -m "feat: add DurationHelper for parsing/formatting rclone durations"
```

---

### Task 2: Add SizeSuffix parsing/formatting helpers to Core

**Files:**
- Create: `RcloneMountManager.Core/Helpers/SizeSuffixHelper.cs`
- Create: `RcloneMountManager.Tests/Helpers/SizeSuffixHelperTests.cs`

**Step 1: Write the failing tests**

In `RcloneMountManager.Tests/Helpers/SizeSuffixHelperTests.cs`:

```csharp
using RcloneMountManager.Core.Helpers;

namespace RcloneMountManager.Tests.Helpers;

public class SizeSuffixHelperTests
{
    [Theory]
    [InlineData("128Mi", 128, "Mi")]
    [InlineData("1Gi", 1, "Gi")]
    [InlineData("256Ki", 256, "Ki")]
    [InlineData("2Ti", 2, "Ti")]
    [InlineData("1024", 1024, "B")]
    [InlineData("0", 0, "B")]
    [InlineData("", 0, "B")]
    [InlineData("off", 0, "B")]
    public void Parse_ValidSuffix_ReturnsComponents(string input, decimal expectedValue, string expectedUnit)
    {
        var (value, unit) = SizeSuffixHelper.Parse(input);
        Assert.Equal(expectedValue, value);
        Assert.Equal(expectedUnit, unit);
    }

    [Theory]
    [InlineData(128, "Mi", "128Mi")]
    [InlineData(1, "Gi", "1Gi")]
    [InlineData(256, "Ki", "256Ki")]
    [InlineData(0, "B", "0")]
    [InlineData(1024, "B", "1024")]
    public void Format_Components_ReturnsRcloneString(decimal value, string unit, string expected)
    {
        Assert.Equal(expected, SizeSuffixHelper.Format(value, unit));
    }

    [Fact]
    public void Parse_Null_ReturnsZeroBytes()
    {
        var (value, unit) = SizeSuffixHelper.Parse(null);
        Assert.Equal(0m, value);
        Assert.Equal("B", unit);
    }

    [Fact]
    public void Units_ContainsAllExpected()
    {
        var units = SizeSuffixHelper.Units;
        Assert.Equal(5, units.Count);
        Assert.Contains("B", units);
        Assert.Contains("Ki", units);
        Assert.Contains("Mi", units);
        Assert.Contains("Gi", units);
        Assert.Contains("Ti", units);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~SizeSuffixHelperTests" -v minimal`
Expected: FAIL — `SizeSuffixHelper` class does not exist

**Step 3: Write implementation**

In `RcloneMountManager.Core/Helpers/SizeSuffixHelper.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RcloneMountManager.Core.Helpers;

public static partial class SizeSuffixHelper
{
    public static IReadOnlyList<string> Units { get; } = ["B", "Ki", "Mi", "Gi", "Ti"];

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)\s*(Ki|Mi|Gi|Ti|B)?$", RegexOptions.Compiled)]
    private static partial Regex SizeRegex();

    public static (decimal Value, string Unit) Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || string.Equals(input, "off", StringComparison.OrdinalIgnoreCase))
            return (0m, "B");

        var match = SizeRegex().Match(input.Trim());
        if (!match.Success)
            return (0m, "B");

        var value = decimal.Parse(match.Groups[1].Value);
        var unit = match.Groups[2].Success && !string.IsNullOrEmpty(match.Groups[2].Value)
            ? match.Groups[2].Value
            : "B";

        return (value, unit);
    }

    public static string Format(decimal value, string unit)
    {
        if (value == 0m && unit == "B") return "0";

        var intValue = (long)value;
        return unit == "B" ? $"{intValue}" : $"{intValue}{unit}";
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~SizeSuffixHelperTests" -v minimal`
Expected: All PASS

**Step 5: Commit**

```bash
git add RcloneMountManager.Core/Helpers/SizeSuffixHelper.cs RcloneMountManager.Tests/Helpers/SizeSuffixHelperTests.cs
git commit -m "feat: add SizeSuffixHelper for parsing/formatting rclone size values"
```

---

### Task 3: Extend MountOptionInputViewModel with typed properties

**Files:**
- Modify: `RcloneMountManager.GUI/ViewModels/MountOptionInputViewModel.cs`

**Step 1: Add typed value properties**

Replace the entire content of `MountOptionInputViewModel.cs` with:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RcloneMountManager.Core.Helpers;
using RcloneMountManager.Core.Models;
using System;
using System.Collections.Generic;

namespace RcloneMountManager.ViewModels;

public partial class MountOptionInputViewModel : ObservableObject
{
    private readonly RcloneOption _option;
    private bool _syncing; // prevent infinite loops during sync

    public MountOptionInputViewModel(RcloneOption option, string? currentValue = null)
    {
        _option = option;
        _value = currentValue ?? string.Empty;
        _isSet = !string.IsNullOrEmpty(currentValue);
        InitializeTypedValues(currentValue);
    }

    public string Name => _option.Name;
    public string FlagName => "--" + _option.Name.Replace('_', '-');
    public string Help => _option.Help;
    public string DefaultStr => _option.DefaultStr;
    public bool IsAdvanced => _option.Advanced;
    public OptionControlType ControlType => _option.GetControlType();
    public IReadOnlyList<string>? EnumValues => _option.GetEnumValues();
    public static IReadOnlyList<string> SizeSuffixUnits => SizeSuffixHelper.Units;

    public string Label
    {
        get
        {
            var flag = FlagName;
            if (_option.Required) flag += " (required)";
            return flag;
        }
    }

    // --- Core string value (used by persistence/command generation) ---
    [ObservableProperty]
    private string _value;

    [ObservableProperty]
    private bool _isSet;

    // --- Typed properties for specific controls ---

    [ObservableProperty]
    private bool _boolValue;

    [ObservableProperty]
    private decimal? _numericValue;

    [ObservableProperty]
    private TimeSpan? _durationValue;

    [ObservableProperty]
    private decimal? _sizeSuffixNumericValue;

    [ObservableProperty]
    private string _sizeSuffixUnit = "B";

    [ObservableProperty]
    private string? _selectedEnumValue;

    // --- Computed ---
    public bool HasNonDefaultValue =>
        IsSet && !string.IsNullOrEmpty(Value) &&
        !string.Equals(Value, DefaultStr, StringComparison.OrdinalIgnoreCase);

    // --- Initialization ---
    private void InitializeTypedValues(string? currentValue)
    {
        _syncing = true;
        try
        {
            switch (ControlType)
            {
                case OptionControlType.Toggle:
                    _boolValue = string.Equals(currentValue, "true", StringComparison.OrdinalIgnoreCase);
                    break;

                case OptionControlType.Numeric:
                    _numericValue = decimal.TryParse(currentValue, out var num) ? num : null;
                    break;

                case OptionControlType.Duration:
                    _durationValue = string.IsNullOrEmpty(currentValue)
                        ? null
                        : DurationHelper.Parse(currentValue);
                    break;

                case OptionControlType.SizeSuffix:
                    if (!string.IsNullOrEmpty(currentValue))
                    {
                        var (sv, su) = SizeSuffixHelper.Parse(currentValue);
                        _sizeSuffixNumericValue = sv;
                        _sizeSuffixUnit = su;
                    }
                    break;

                case OptionControlType.ComboBox:
                    _selectedEnumValue = string.IsNullOrEmpty(currentValue) ? null : currentValue;
                    break;
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    // --- Sync: typed → string ---
    partial void OnBoolValueChanged(bool value)
    {
        if (_syncing) return;
        SyncToString(value ? "true" : "false");
    }

    partial void OnNumericValueChanged(decimal? value)
    {
        if (_syncing) return;
        SyncToString(value?.ToString() ?? string.Empty);
    }

    partial void OnDurationValueChanged(TimeSpan? value)
    {
        if (_syncing) return;
        SyncToString(value.HasValue ? DurationHelper.Format(value.Value) : string.Empty);
    }

    partial void OnSizeSuffixNumericValueChanged(decimal? value)
    {
        if (_syncing) return;
        SyncSizeSuffix();
    }

    partial void OnSizeSuffixUnitChanged(string value)
    {
        if (_syncing) return;
        SyncSizeSuffix();
    }

    partial void OnSelectedEnumValueChanged(string? value)
    {
        if (_syncing) return;
        SyncToString(value ?? string.Empty);
    }

    private void SyncSizeSuffix()
    {
        var val = SizeSuffixNumericValue ?? 0m;
        SyncToString(SizeSuffixHelper.Format(val, SizeSuffixUnit));
    }

    private void SyncToString(string newValue)
    {
        _syncing = true;
        try
        {
            Value = newValue;
            if (!string.IsNullOrEmpty(newValue) &&
                !string.Equals(newValue, DefaultStr, StringComparison.OrdinalIgnoreCase))
            {
                IsSet = true;
            }
            OnPropertyChanged(nameof(HasNonDefaultValue));
        }
        finally
        {
            _syncing = false;
        }
    }

    // --- Sync: string → typed (for external Value changes, e.g., reset) ---
    partial void OnValueChanged(string value)
    {
        if (_syncing) return;

        if (!string.IsNullOrEmpty(value) &&
            !string.Equals(value, DefaultStr, StringComparison.OrdinalIgnoreCase))
        {
            IsSet = true;
        }
        OnPropertyChanged(nameof(HasNonDefaultValue));

        _syncing = true;
        try
        {
            switch (ControlType)
            {
                case OptionControlType.Toggle:
                    BoolValue = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    break;
                case OptionControlType.Numeric:
                    NumericValue = decimal.TryParse(value, out var n) ? n : null;
                    break;
                case OptionControlType.Duration:
                    DurationValue = string.IsNullOrEmpty(value) ? null : DurationHelper.Parse(value);
                    break;
                case OptionControlType.SizeSuffix:
                    if (!string.IsNullOrEmpty(value))
                    {
                        var (sv, su) = SizeSuffixHelper.Parse(value);
                        SizeSuffixNumericValue = sv;
                        SizeSuffixUnit = su;
                    }
                    else
                    {
                        SizeSuffixNumericValue = null;
                        SizeSuffixUnit = "B";
                    }
                    break;
                case OptionControlType.ComboBox:
                    SelectedEnumValue = string.IsNullOrEmpty(value) ? null : value;
                    break;
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    [RelayCommand]
    private void ResetToDefault()
    {
        Value = string.Empty;
        IsSet = false;
        OnPropertyChanged(nameof(HasNonDefaultValue));
    }
}
```

**Step 2: Verify build compiles**

Run: `dotnet build`
Expected: Build succeeded

**Step 3: Run all existing tests to verify no regressions**

Run: `dotnet test -v minimal`
Expected: All existing tests PASS

**Step 4: Commit**

```bash
git add RcloneMountManager.GUI/ViewModels/MountOptionInputViewModel.cs
git commit -m "feat: add typed value properties to MountOptionInputViewModel"
```

---

### Task 4: Create OptionControlTemplateSelector

**Files:**
- Create: `RcloneMountManager.GUI/Controls/OptionControlTemplateSelector.cs`

**Step 1: Write the DataTemplateSelector**

```csharp
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;
using RcloneMountManager.Core.Models;
using RcloneMountManager.ViewModels;
using System.Collections.Generic;

namespace RcloneMountManager.Controls;

public class OptionControlTemplateSelector : IDataTemplate
{
    [Content]
    public Dictionary<string, IDataTemplate> Templates { get; } = new();

    public Control? Build(object? param)
    {
        if (param is not MountOptionInputViewModel vm)
            return null;

        var key = vm.ControlType switch
        {
            OptionControlType.Toggle => "Toggle",
            OptionControlType.ComboBox => "ComboBox",
            OptionControlType.Numeric => "Numeric",
            OptionControlType.Duration => "Duration",
            OptionControlType.SizeSuffix => "SizeSuffix",
            _ => "Text",
        };

        return Templates.TryGetValue(key, out var template)
            ? template.Build(param)
            : null;
    }

    public bool Match(object? data) => data is MountOptionInputViewModel;
}
```

**Step 2: Verify build**

Run: `dotnet build`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add RcloneMountManager.GUI/Controls/OptionControlTemplateSelector.cs
git commit -m "feat: add OptionControlTemplateSelector for type-aware parameter rendering"
```

---

### Task 5: Rewrite MountOptionsView.axaml with typed templates and visual polish

**Files:**
- Modify: `RcloneMountManager.GUI/Views/MountOptionsView.axaml`

**Step 1: Replace the entire AXAML file**

Replace the full content of `MountOptionsView.axaml` with the new version that includes:
- All 6 DataTemplates (Toggle, ComboBox, Numeric, Duration, SizeSuffix, Text)
- The OptionControlTemplateSelector
- Visual polish (accent borders, improved typography, reset icon, separators)

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:RcloneMountManager.ViewModels"
             xmlns:controls="using:RcloneMountManager.Controls"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d"
             x:Class="RcloneMountManager.Views.MountOptionsView"
             x:DataType="vm:MountOptionsViewModel">

    <UserControl.Resources>
        <!-- Shared row wrapper used by all templates -->

        <!-- TOGGLE (bool) -->
        <DataTemplate x:Key="Toggle" x:DataType="vm:MountOptionInputViewModel">
            <Border Padding="8,6" Margin="0,0,0,1"
                    BorderThickness="3,0,0,0"
                    CornerRadius="4"
                    BorderBrush="{Binding HasNonDefaultValue, Converter={x:Static BoolConverters.ToSelector}, ConverterParameter={DynamicResource SystemAccentColor}|Transparent}">
                <Grid ColumnDefinitions="220,*,Auto" RowDefinitions="Auto,Auto">
                    <TextBlock Grid.Row="0" Grid.Column="0"
                               Text="{Binding Label}"
                               VerticalAlignment="Center"
                               FontSize="12"
                               FontWeight="{Binding HasNonDefaultValue, Converter={x:Static BoolConverters.ToSelector}, ConverterParameter=SemiBold|Normal}"/>
                    <ToggleSwitch Grid.Row="0" Grid.Column="1"
                                  IsChecked="{Binding BoolValue}"
                                  OnContent="Enabled" OffContent="Disabled"
                                  Margin="8,0,0,0"/>
                    <Button Grid.Row="0" Grid.Column="2"
                            Command="{Binding ResetToDefaultCommand}"
                            Padding="6,4" Margin="4,0,0,0" FontSize="10"
                            IsVisible="{Binding HasNonDefaultValue}"
                            ToolTip.Tip="{Binding DefaultStr, StringFormat='Reset to default ({0})'}">
                        <PathIcon Data="M17.65 6.35A8 8 0 1 0 20 12h-2a6 6 0 1 1-1.76-4.24L13 11h7V4l-2.35 2.35z"
                                  Width="12" Height="12"/>
                    </Button>
                    <TextBlock Grid.Row="1" Grid.Column="0" Grid.ColumnSpan="3"
                               Text="{Binding Help}" TextWrapping="Wrap"
                               FontSize="11" Opacity="0.5" FontStyle="Italic"
                               Margin="0,2,0,4"
                               MaxLines="2" TextTrimming="CharacterEllipsis"
                               ToolTip.Tip="{Binding Help}"/>
                </Grid>
            </Border>
        </DataTemplate>

        <!-- COMBOBOX (enum) -->
        <DataTemplate x:Key="ComboBox" x:DataType="vm:MountOptionInputViewModel">
            <Border Padding="8,6" Margin="0,0,0,1"
                    BorderThickness="3,0,0,0"
                    CornerRadius="4"
                    BorderBrush="{Binding HasNonDefaultValue, Converter={x:Static BoolConverters.ToSelector}, ConverterParameter={DynamicResource SystemAccentColor}|Transparent}">
                <Grid ColumnDefinitions="220,*,Auto" RowDefinitions="Auto,Auto">
                    <TextBlock Grid.Row="0" Grid.Column="0"
                               Text="{Binding Label}"
                               VerticalAlignment="Center"
                               FontSize="12"
                               FontWeight="{Binding HasNonDefaultValue, Converter={x:Static BoolConverters.ToSelector}, ConverterParameter=SemiBold|Normal}"/>
                    <ComboBox Grid.Row="0" Grid.Column="1"
                              ItemsSource="{Binding EnumValues}"
                              SelectedItem="{Binding SelectedEnumValue}"
                              PlaceholderText="{Binding DefaultStr}"
                              Margin="8,0,0,0"
                              MinWidth="200"
                              HorizontalAlignment="Stretch"/>
                    <Button Grid.Row="0" Grid.Column="2"
                            Command="{Binding ResetToDefaultCommand}"
                            Padding="6,4" Margin="4,0,0,0" FontSize="10"
                            IsVisible="{Binding HasNonDefaultValue}"
                            ToolTip.Tip="{Binding DefaultStr, StringFormat='Reset to default ({0})'}">
                        <PathIcon Data="M17.65 6.35A8 8 0 1 0 20 12h-2a6 6 0 1 1-1.76-4.24L13 11h7V4l-2.35 2.35z"
                                  Width="12" Height="12"/>
                    </Button>
                    <TextBlock Grid.Row="1" Grid.Column="0" Grid.ColumnSpan="3"
                               Text="{Binding Help}" TextWrapping="Wrap"
                               FontSize="11" Opacity="0.5" FontStyle="Italic"
                               Margin="0,2,0,4"
                               MaxLines="2" TextTrimming="CharacterEllipsis"
                               ToolTip.Tip="{Binding Help}"/>
                </Grid>
            </Border>
        </DataTemplate>

        <!-- NUMERIC (int, float) -->
        <DataTemplate x:Key="Numeric" x:DataType="vm:MountOptionInputViewModel">
            <Border Padding="8,6" Margin="0,0,0,1"
                    BorderThickness="3,0,0,0"
                    CornerRadius="4"
                    BorderBrush="{Binding HasNonDefaultValue, Converter={x:Static BoolConverters.ToSelector}, ConverterParameter={DynamicResource SystemAccentColor}|Transparent}">
                <Grid ColumnDefinitions="220,*,Auto" RowDefinitions="Auto,Auto">
                    <TextBlock Grid.Row="0" Grid.Column="0"
                               Text="{Binding Label}"
                               VerticalAlignment="Center"
                               FontSize="12"
                               FontWeight="{Binding HasNonDefaultValue, Converter={x:Static BoolConverters.ToSelector}, ConverterParameter=SemiBold|Normal}"/>
                    <NumericUpDown Grid.Row="0" Grid.Column="1"
                                   Value="{Binding NumericValue}"
                                   Watermark="{Binding DefaultStr}"
                                   Minimum="0"
                                   Increment="1"
                                   Margin="8,0,0,0"
                                   MinWidth="200"
                                   HorizontalAlignment="Stretch"
                                   ShowButtonSpinner="True"/>
                    <Button Grid.Row="0" Grid.Column="2"
                            Command="{Binding ResetToDefaultCommand}"
                            Padding="6,4" Margin="4,0,0,0" FontSize="10"
                            IsVisible="{Binding HasNonDefaultValue}"
                            ToolTip.Tip="{Binding DefaultStr, StringFormat='Reset to default ({0})'}">
                        <PathIcon Data="M17.65 6.35A8 8 0 1 0 20 12h-2a6 6 0 1 1-1.76-4.24L13 11h7V4l-2.35 2.35z"
                                  Width="12" Height="12"/>
                    </Button>
                    <TextBlock Grid.Row="1" Grid.Column="0" Grid.ColumnSpan="3"
                               Text="{Binding Help}" TextWrapping="Wrap"
                               FontSize="11" Opacity="0.5" FontStyle="Italic"
                               Margin="0,2,0,4"
                               MaxLines="2" TextTrimming="CharacterEllipsis"
                               ToolTip.Tip="{Binding Help}"/>
                </Grid>
            </Border>
        </DataTemplate>

        <!-- DURATION (TimePicker) -->
        <DataTemplate x:Key="Duration" x:DataType="vm:MountOptionInputViewModel">
            <Border Padding="8,6" Margin="0,0,0,1"
                    BorderThickness="3,0,0,0"
                    CornerRadius="4"
                    BorderBrush="{Binding HasNonDefaultValue, Converter={x:Static BoolConverters.ToSelector}, ConverterParameter={DynamicResource SystemAccentColor}|Transparent}">
                <Grid ColumnDefinitions="220,*,Auto" RowDefinitions="Auto,Auto">
                    <TextBlock Grid.Row="0" Grid.Column="0"
                               Text="{Binding Label}"
                               VerticalAlignment="Center"
                               FontSize="12"
                               FontWeight="{Binding HasNonDefaultValue, Converter={x:Static BoolConverters.ToSelector}, ConverterParameter=SemiBold|Normal}"/>
                    <TimePicker Grid.Row="0" Grid.Column="1"
                                SelectedTime="{Binding DurationValue}"
                                ClockIdentifier="24HourClock"
                                UseSeconds="True"
                                MinuteIncrement="1"
                                Margin="8,0,0,0"
                                MinWidth="200"
                                HorizontalAlignment="Stretch"/>
                    <Button Grid.Row="0" Grid.Column="2"
                            Command="{Binding ResetToDefaultCommand}"
                            Padding="6,4" Margin="4,0,0,0" FontSize="10"
                            IsVisible="{Binding HasNonDefaultValue}"
                            ToolTip.Tip="{Binding DefaultStr, StringFormat='Reset to default ({0})'}">
                        <PathIcon Data="M17.65 6.35A8 8 0 1 0 20 12h-2a6 6 0 1 1-1.76-4.24L13 11h7V4l-2.35 2.35z"
                                  Width="12" Height="12"/>
                    </Button>
                    <TextBlock Grid.Row="1" Grid.Column="0" Grid.ColumnSpan="3"
                               Text="{Binding Help}" TextWrapping="Wrap"
                               FontSize="11" Opacity="0.5" FontStyle="Italic"
                               Margin="0,2,0,4"
                               MaxLines="2" TextTrimming="CharacterEllipsis"
                               ToolTip.Tip="{Binding Help}"/>
                </Grid>
            </Border>
        </DataTemplate>

        <!-- SIZE SUFFIX (NumericUpDown + ComboBox unit) -->
        <DataTemplate x:Key="SizeSuffix" x:DataType="vm:MountOptionInputViewModel">
            <Border Padding="8,6" Margin="0,0,0,1"
                    BorderThickness="3,0,0,0"
                    CornerRadius="4"
                    BorderBrush="{Binding HasNonDefaultValue, Converter={x:Static BoolConverters.ToSelector}, ConverterParameter={DynamicResource SystemAccentColor}|Transparent}">
                <Grid ColumnDefinitions="220,*,Auto" RowDefinitions="Auto,Auto">
                    <TextBlock Grid.Row="0" Grid.Column="0"
                               Text="{Binding Label}"
                               VerticalAlignment="Center"
                               FontSize="12"
                               FontWeight="{Binding HasNonDefaultValue, Converter={x:Static BoolConverters.ToSelector}, ConverterParameter=SemiBold|Normal}"/>
                    <Grid Grid.Row="0" Grid.Column="1" ColumnDefinitions="*,Auto" Margin="8,0,0,0">
                        <NumericUpDown Grid.Column="0"
                                       Value="{Binding SizeSuffixNumericValue}"
                                       Watermark="{Binding DefaultStr}"
                                       Minimum="0"
                                       Increment="1"
                                       ShowButtonSpinner="True"
                                       MinWidth="120"/>
                        <ComboBox Grid.Column="1"
                                  ItemsSource="{Binding SizeSuffixUnits}"
                                  SelectedItem="{Binding SizeSuffixUnit}"
                                  Width="80"
                                  Margin="4,0,0,0"/>
                    </Grid>
                    <Button Grid.Row="0" Grid.Column="2"
                            Command="{Binding ResetToDefaultCommand}"
                            Padding="6,4" Margin="4,0,0,0" FontSize="10"
                            IsVisible="{Binding HasNonDefaultValue}"
                            ToolTip.Tip="{Binding DefaultStr, StringFormat='Reset to default ({0})'}">
                        <PathIcon Data="M17.65 6.35A8 8 0 1 0 20 12h-2a6 6 0 1 1-1.76-4.24L13 11h7V4l-2.35 2.35z"
                                  Width="12" Height="12"/>
                    </Button>
                    <TextBlock Grid.Row="1" Grid.Column="0" Grid.ColumnSpan="3"
                               Text="{Binding Help}" TextWrapping="Wrap"
                               FontSize="11" Opacity="0.5" FontStyle="Italic"
                               Margin="0,2,0,4"
                               MaxLines="2" TextTrimming="CharacterEllipsis"
                               ToolTip.Tip="{Binding Help}"/>
                </Grid>
            </Border>
        </DataTemplate>

        <!-- TEXT (string, fallback) -->
        <DataTemplate x:Key="Text" x:DataType="vm:MountOptionInputViewModel">
            <Border Padding="8,6" Margin="0,0,0,1"
                    BorderThickness="3,0,0,0"
                    CornerRadius="4"
                    BorderBrush="{Binding HasNonDefaultValue, Converter={x:Static BoolConverters.ToSelector}, ConverterParameter={DynamicResource SystemAccentColor}|Transparent}">
                <Grid ColumnDefinitions="220,*,Auto" RowDefinitions="Auto,Auto">
                    <TextBlock Grid.Row="0" Grid.Column="0"
                               Text="{Binding Label}"
                               VerticalAlignment="Center"
                               FontSize="12"
                               FontWeight="{Binding HasNonDefaultValue, Converter={x:Static BoolConverters.ToSelector}, ConverterParameter=SemiBold|Normal}"/>
                    <TextBox Grid.Row="0" Grid.Column="1"
                             Text="{Binding Value}"
                             Watermark="{Binding DefaultStr}"
                             Margin="8,0,0,0"/>
                    <Button Grid.Row="0" Grid.Column="2"
                            Command="{Binding ResetToDefaultCommand}"
                            Padding="6,4" Margin="4,0,0,0" FontSize="10"
                            IsVisible="{Binding HasNonDefaultValue}"
                            ToolTip.Tip="{Binding DefaultStr, StringFormat='Reset to default ({0})'}">
                        <PathIcon Data="M17.65 6.35A8 8 0 1 0 20 12h-2a6 6 0 1 1-1.76-4.24L13 11h7V4l-2.35 2.35z"
                                  Width="12" Height="12"/>
                    </Button>
                    <TextBlock Grid.Row="1" Grid.Column="0" Grid.ColumnSpan="3"
                               Text="{Binding Help}" TextWrapping="Wrap"
                               FontSize="11" Opacity="0.5" FontStyle="Italic"
                               Margin="0,2,0,4"
                               MaxLines="2" TextTrimming="CharacterEllipsis"
                               ToolTip.Tip="{Binding Help}"/>
                </Grid>
            </Border>
        </DataTemplate>

        <controls:OptionControlTemplateSelector x:Key="OptionTemplateSelector">
            <controls:OptionControlTemplateSelector.Templates>
                <x:String x:Key="__keyToggle">Toggle</x:String>
                <!-- See code-behind approach below -->
            </controls:OptionControlTemplateSelector.Templates>
        </controls:OptionControlTemplateSelector>
    </UserControl.Resources>

    <StackPanel Spacing="8">
        <Grid ColumnDefinitions="*,Auto" Margin="0,0,0,8">
            <TextBlock Text="Mount Parameters" FontWeight="SemiBold" FontSize="14" VerticalAlignment="Center"/>
            <CheckBox Grid.Column="1" Content="Show advanced options" IsChecked="{Binding ShowAdvancedOptions}"/>
        </Grid>

        <TextBlock Text="Loading rclone options..."
                   IsVisible="{Binding IsLoading}"
                   Opacity="0.7"
                   FontStyle="Italic"/>

        <ItemsControl ItemsSource="{Binding Groups}" IsVisible="{Binding !IsLoading}">
            <ItemsControl.ItemTemplate>
                <DataTemplate x:DataType="vm:MountOptionGroupViewModel">
                    <Expander IsExpanded="{Binding IsExpanded}"
                              Margin="0,0,0,4">
                        <Expander.Header>
                            <Grid ColumnDefinitions="*,Auto">
                                <TextBlock Text="{Binding DisplayName}" FontWeight="SemiBold"/>
                                <Border Grid.Column="1"
                                        Background="{DynamicResource SystemAccentColor}"
                                        CornerRadius="10"
                                        Padding="8,2"
                                        Margin="8,0,0,0"
                                        IsVisible="{Binding HasModifiedOptions}">
                                    <TextBlock Text="{Binding ModifiedCount}"
                                               FontSize="11"
                                               Foreground="White"
                                               FontWeight="SemiBold"/>
                                </Border>
                            </Grid>
                        </Expander.Header>
                        <ItemsControl ItemsSource="{Binding VisibleOptions}"
                                      Margin="0,4,0,0"
                                      ItemTemplate="{StaticResource OptionTemplateSelector}">
                        </ItemsControl>
                    </Expander>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </StackPanel>
</UserControl>
```

**Important note:** The AXAML dictionary approach for `OptionControlTemplateSelector` templates won't work directly — the templates dictionary is populated in code-behind. The selector needs its template references. The cleanest approach: register templates in `MountOptionsView.axaml.cs` code-behind, or use the `FuncDataTemplate` approach in the selector itself. This will be refined during implementation — the key structure and visual design is as shown.

**Step 2: Verify build**

Run: `dotnet build`
Expected: Build succeeded (may need iteration on AXAML specifics)

**Step 3: Commit**

```bash
git add RcloneMountManager.GUI/Views/MountOptionsView.axaml
git commit -m "feat: rewrite MountOptionsView with type-specific controls and visual polish"
```

---

### Task 6: Update MountOptionGroupViewModel with badge support

**Files:**
- Modify: `RcloneMountManager.GUI/ViewModels/MountOptionsViewModel.cs`

**Step 1: Add badge-related properties to MountOptionGroupViewModel**

Add these properties to the `MountOptionGroupViewModel` class (at the bottom of `MountOptionsViewModel.cs`):

```csharp
public int ModifiedCount => VisibleOptions.Count(o => o.HasNonDefaultValue);
public bool HasModifiedOptions => ModifiedCount > 0;

// Keep existing Header for backward compat, but update format
public string Header => $"{DisplayName} ({ModifiedCount}/{VisibleOptions.Count()})";
```

**Step 2: Verify build**

Run: `dotnet build`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add RcloneMountManager.GUI/ViewModels/MountOptionsViewModel.cs
git commit -m "feat: add modified-count badge properties to option group ViewModel"
```

---

### Task 7: Wire up the template selector in code-behind

**Files:**
- Modify: `RcloneMountManager.GUI/Views/MountOptionsView.axaml.cs`

**Step 1: Register templates in code-behind**

```csharp
using Avalonia.Controls;

namespace RcloneMountManager.Views;

public partial class MountOptionsView : UserControl
{
    public MountOptionsView()
    {
        InitializeComponent();

        // Wire up templates from resources into the selector
        if (Resources["OptionTemplateSelector"] is Controls.OptionControlTemplateSelector selector)
        {
            var keys = new[] { "Toggle", "ComboBox", "Numeric", "Duration", "SizeSuffix", "Text" };
            foreach (var key in keys)
            {
                if (Resources[key] is Avalonia.Controls.Templates.IDataTemplate template)
                {
                    selector.Templates[key] = template;
                }
            }
        }
    }
}
```

**Step 2: Verify build and run**

Run: `dotnet build && dotnet run --project RcloneMountManager.GUI`
Expected: App launches, parameters show type-specific controls

**Step 3: Commit**

```bash
git add RcloneMountManager.GUI/Views/MountOptionsView.axaml.cs
git commit -m "feat: wire OptionControlTemplateSelector templates in code-behind"
```

---

### Task 8: Add ViewModel tests for typed property sync

**Files:**
- Create: `RcloneMountManager.Tests/ViewModels/MountOptionInputViewModelTests.cs`

**Step 1: Write tests**

```csharp
using RcloneMountManager.Core.Models;
using RcloneMountManager.ViewModels;

namespace RcloneMountManager.Tests.ViewModels;

public class MountOptionInputViewModelTests
{
    [Fact]
    public void Toggle_BoolValue_SyncsToValueString()
    {
        var option = new RcloneOption { Name = "debug_fuse", Type = "bool" };
        var vm = new MountOptionInputViewModel(option);

        vm.BoolValue = true;

        Assert.Equal("true", vm.Value);
        Assert.True(vm.IsSet);
    }

    [Fact]
    public void Toggle_InitWithTrue_SetsBoolValue()
    {
        var option = new RcloneOption { Name = "debug_fuse", Type = "bool" };
        var vm = new MountOptionInputViewModel(option, "true");

        Assert.True(vm.BoolValue);
        Assert.True(vm.IsSet);
    }

    [Fact]
    public void Numeric_NumericValue_SyncsToValueString()
    {
        var option = new RcloneOption { Name = "transfers", Type = "int" };
        var vm = new MountOptionInputViewModel(option);

        vm.NumericValue = 8;

        Assert.Equal("8", vm.Value);
        Assert.True(vm.IsSet);
    }

    [Fact]
    public void Duration_DurationValue_SyncsToValueString()
    {
        var option = new RcloneOption { Name = "dir_cache_time", Type = "Duration" };
        var vm = new MountOptionInputViewModel(option);

        vm.DurationValue = new TimeSpan(1, 30, 0);

        Assert.Equal("1h30m", vm.Value);
    }

    [Fact]
    public void Duration_InitWithString_SetsDurationValue()
    {
        var option = new RcloneOption { Name = "dir_cache_time", Type = "Duration" };
        var vm = new MountOptionInputViewModel(option, "5m30s");

        Assert.Equal(new TimeSpan(0, 5, 30), vm.DurationValue);
    }

    [Fact]
    public void SizeSuffix_Components_SyncToValueString()
    {
        var option = new RcloneOption { Name = "buffer_size", Type = "SizeSuffix" };
        var vm = new MountOptionInputViewModel(option);

        vm.SizeSuffixNumericValue = 128;
        vm.SizeSuffixUnit = "Mi";

        Assert.Equal("128Mi", vm.Value);
    }

    [Fact]
    public void SizeSuffix_InitWithString_SetsComponents()
    {
        var option = new RcloneOption { Name = "buffer_size", Type = "SizeSuffix" };
        var vm = new MountOptionInputViewModel(option, "256Gi");

        Assert.Equal(256m, vm.SizeSuffixNumericValue);
        Assert.Equal("Gi", vm.SizeSuffixUnit);
    }

    [Fact]
    public void ComboBox_SelectedEnumValue_SyncsToValueString()
    {
        var option = new RcloneOption { Name = "vfs_cache_mode", Type = "CacheMode" };
        var vm = new MountOptionInputViewModel(option);

        vm.SelectedEnumValue = "full";

        Assert.Equal("full", vm.Value);
    }

    [Fact]
    public void ResetToDefault_ClearsAllTypedValues()
    {
        var option = new RcloneOption { Name = "transfers", Type = "int" };
        var vm = new MountOptionInputViewModel(option, "8");

        vm.ResetToDefault();

        Assert.Equal(string.Empty, vm.Value);
        Assert.False(vm.IsSet);
        Assert.Null(vm.NumericValue);
    }
}
```

**Step 2: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~MountOptionInputViewModelTests" -v minimal`
Expected: All PASS

**Step 3: Commit**

```bash
git add RcloneMountManager.Tests/ViewModels/MountOptionInputViewModelTests.cs
git commit -m "test: add ViewModel tests for typed property bidirectional sync"
```

---

### Task 9: Full integration test — build, run tests, manual verification

**Step 1: Run all tests**

Run: `dotnet test -v minimal`
Expected: All tests PASS (helpers, models, viewmodels)

**Step 2: Build the app**

Run: `dotnet build`
Expected: Build succeeded with 0 errors

**Step 3: Launch and verify visually**

Run: `dotnet run --project RcloneMountManager.GUI`
Expected:
- Bool params show ToggleSwitch with Enabled/Disabled labels
- Enum params show ComboBox dropdowns with correct values
- Numeric params show NumericUpDown with spinner buttons
- Duration params show TimePicker with hours/minutes/seconds
- SizeSuffix params show NumericUpDown + unit ComboBox
- String params show TextBox (unchanged)
- Modified values show left accent border
- Reset button shows undo icon with tooltip
- Group headers show badge with modified count

**Step 4: Commit any fixes**

```bash
git add -A
git commit -m "fix: integration fixes for typed parameter controls"
```

---

### Task 10: Final commit — squash or tag

**Step 1: Verify clean working tree**

Run: `git status`
Expected: `nothing to commit, working tree clean`

**Step 2: Tag the release**

```bash
git tag -a v0.2.0 -m "feat: typed parameter controls with visual polish"
```
