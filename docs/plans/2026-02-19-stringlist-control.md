# StringList Control Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a dynamic list control for `CommaSepList`, `SpaceSepList`, and `stringArray` option types, with key-value mode for headers.

**Architecture:** New `OptionControlType.StringList` with an `ObservableCollection<StringListItemViewModel>` on `TypedOptionViewModel`. A new `StringList` DataTemplate in App.axaml renders each item as a TextBox (or Key+Value pair for headers) with add/remove buttons. Serialization uses the correct separator per type. Detection of key-value mode is by option name containing "headers".

**Tech Stack:** Avalonia 11.3, .NET 10, CommunityToolkit.Mvvm, xUnit

---

### Task 1: Add StringList to OptionControlType and detection logic

**Files:**
- Modify: `RcloneMountManager.Core/Models/RcloneOption.cs`
- Modify: `RcloneMountManager.Core/Models/RcloneBackendOption.cs`
- Modify: `RcloneMountManager.Core/Models/IRcloneOptionDefinition.cs`
- Test: `RcloneMountManager.Tests/Models/RcloneBackendOptionTests.cs`

**Step 1: Add `StringList` to `OptionControlType` enum**

In `RcloneMountManager.Core/Models/RcloneOption.cs`, add `StringList` to the enum (after `ComboBox`):

```csharp
public enum OptionControlType
{
    Text,
    Toggle,
    Numeric,
    Duration,
    SizeSuffix,
    ComboBox,
    StringList,
}
```

**Step 2: Add `ListSeparator` to `IRcloneOptionDefinition`**

In `RcloneMountManager.Core/Models/IRcloneOptionDefinition.cs`, add:

```csharp
string ListSeparator { get; }
bool IsKeyValue { get; }
```

**Step 3: Update `RcloneOption.GetControlType()` to return `StringList`**

Change line 41 from:
```csharp
"stringArray" or "SpaceSepList" => OptionControlType.Text,
```
to:
```csharp
"stringArray" or "SpaceSepList" or "CommaSepList" => OptionControlType.StringList,
```

Also remove `"CommaSepList"` if it falls through to the `_` default. Add `CommaSepList` to the match.

Add the interface properties:
```csharp
public string ListSeparator => Type switch
{
    "CommaSepList" => ",",
    "SpaceSepList" => " ",
    "stringArray" => ",",
    _ => ",",
};

public bool IsKeyValue => Name.Contains("header", StringComparison.OrdinalIgnoreCase);
```

**Step 4: Update `RcloneBackendOption.GetControlType()` to return `StringList`**

Add before the `_ => OptionControlType.Text` in the type switch:
```csharp
"CommaSepList" or "SpaceSepList" or "stringArray" => OptionControlType.StringList,
```

Add the interface properties:
```csharp
public string ListSeparator => Type switch
{
    "CommaSepList" => ",",
    "SpaceSepList" => " ",
    "stringArray" => ",",
    _ => ",",
};

public bool IsKeyValue => Name.Contains("header", StringComparison.OrdinalIgnoreCase);
```

**Step 5: Write tests**

Add to `RcloneBackendOptionTests.cs`:

```csharp
[Theory]
[InlineData("CommaSepList")]
[InlineData("SpaceSepList")]
[InlineData("stringArray")]
public void GetControlType_ForListTypes_ReturnsStringList(string type)
{
    var option = new RcloneBackendOption { Type = type };
    Assert.Equal(OptionControlType.StringList, option.GetControlType());
}

[Fact]
public void IsKeyValue_ForHeaders_ReturnsTrue()
{
    var option = new RcloneBackendOption { Name = "headers", Type = "CommaSepList" };
    Assert.True(option.IsKeyValue);
}

[Fact]
public void IsKeyValue_ForNonHeaders_ReturnsFalse()
{
    var option = new RcloneBackendOption { Name = "hashes", Type = "CommaSepList" };
    Assert.False(option.IsKeyValue);
}
```

Also add a mount option test in `MountOptionInputViewModelTests.cs`:

```csharp
[Fact]
public void StringList_ControlType_DetectedForStringArray()
{
    var option = new RcloneOption { Name = "exclude", Type = "stringArray" };
    var vm = new MountOptionInputViewModel(option);
    Assert.Equal(OptionControlType.StringList, vm.ControlType);
}
```

**Step 6: Run tests**

Run: `dotnet test`
Expected: All tests pass (new + existing)

**Step 7: Commit**

```
feat: add StringList to OptionControlType with detection for list types
```

---

### Task 2: StringListItemViewModel and TypedOptionViewModel integration

**Files:**
- Create: `RcloneMountManager.Core/ViewModels/StringListItemViewModel.cs`
- Modify: `RcloneMountManager.Core/ViewModels/TypedOptionViewModel.cs`
- Test: `RcloneMountManager.Tests/ViewModels/MountOptionInputViewModelTests.cs`

**Step 1: Create `StringListItemViewModel`**

Create `RcloneMountManager.Core/ViewModels/StringListItemViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace RcloneMountManager.Core.ViewModels;

public partial class StringListItemViewModel : ObservableObject
{
    private readonly Action<StringListItemViewModel> _removeAction;
    private readonly Action _syncAction;
    private readonly bool _isKeyValue;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private string _key = string.Empty;

    [ObservableProperty]
    private string _itemValue = string.Empty;

    public bool IsKeyValue => _isKeyValue;

    public StringListItemViewModel(Action<StringListItemViewModel> removeAction, Action syncAction, bool isKeyValue)
    {
        _removeAction = removeAction;
        _syncAction = syncAction;
        _isKeyValue = isKeyValue;
    }

    partial void OnTextChanged(string value) => _syncAction();
    partial void OnKeyChanged(string value) => _syncAction();
    partial void OnItemValueChanged(string value) => _syncAction();

    [RelayCommand]
    private void Remove() => _removeAction(this);

    /// <summary>
    /// Returns the serialized form: plain text for simple items, "Key: Value" for headers.
    /// </summary>
    public string Serialize()
    {
        if (_isKeyValue)
        {
            var k = Key.Trim();
            var v = ItemValue.Trim();
            if (string.IsNullOrEmpty(k) && string.IsNullOrEmpty(v)) return string.Empty;
            return $"{k}: {v}";
        }
        return Text.Trim();
    }

    /// <summary>
    /// Parses a serialized string into this item.
    /// </summary>
    public void Deserialize(string value)
    {
        if (_isKeyValue)
        {
            var colonIndex = value.IndexOf(':');
            if (colonIndex >= 0)
            {
                Key = value[..colonIndex].Trim();
                ItemValue = value[(colonIndex + 1)..].Trim();
            }
            else
            {
                Key = value.Trim();
                ItemValue = string.Empty;
            }
        }
        else
        {
            Text = value.Trim();
        }
    }
}
```

**Step 2: Add StringList properties to `TypedOptionViewModel`**

In `TypedOptionViewModel.cs`, add these using statements at the top:
```csharp
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
```

Add after the `_selectedEnumValue` field (around line 45):

```csharp
public ObservableCollection<StringListItemViewModel> StringListItems { get; } = new();

public bool IsKeyValue => Option.IsKeyValue;
public string ListSeparator => Option.ListSeparator;
```

**Step 3: Add `AddStringListItem` command**

Add to `TypedOptionViewModel`:

```csharp
[RelayCommand]
private void AddStringListItem()
{
    var item = CreateStringListItem();
    StringListItems.Add(item);
    // Don't sync here — empty item shouldn't change Value
}

private StringListItemViewModel CreateStringListItem()
{
    return new StringListItemViewModel(
        removeAction: item =>
        {
            StringListItems.Remove(item);
            SyncStringListToValue();
        },
        syncAction: SyncStringListToValue,
        isKeyValue: Option.IsKeyValue
    );
}

private void SyncStringListToValue()
{
    if (_syncing) return;

    var serialized = StringListItems
        .Select(item => item.Serialize())
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .ToList();

    var separator = Option.ListSeparator;
    SyncToString(serialized.Count > 0 ? string.Join(separator, serialized) : string.Empty);
}
```

**Step 4: Add StringList case to `InitializeTypedValues`**

Add a new case in the switch:
```csharp
case OptionControlType.StringList:
    InitializeStringListFromValue(currentValue);
    break;
```

Add the helper method:
```csharp
private void InitializeStringListFromValue(string? value)
{
    StringListItems.Clear();
    if (string.IsNullOrWhiteSpace(value)) return;

    var separator = Option.ListSeparator;
    var parts = value.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    foreach (var part in parts)
    {
        var item = CreateStringListItem();
        item.Deserialize(part);
        StringListItems.Add(item);
    }
}
```

**Step 5: Add StringList case to `OnValueChanged`**

In the switch inside `OnValueChanged`, add:
```csharp
case OptionControlType.StringList:
    InitializeStringListFromValue(value);
    break;
```

**Step 6: Write tests**

Add to `MountOptionInputViewModelTests.cs`:

```csharp
[Fact]
public void StringList_InitWithCommaSeparated_PopulatesItems()
{
    var option = new RcloneOption { Name = "hashes", Type = "CommaSepList" };
    var vm = new MountOptionInputViewModel(option, "md5,sha1,crc32");

    Assert.Equal(3, vm.StringListItems.Count);
    Assert.Equal("md5", vm.StringListItems[0].Text);
    Assert.Equal("sha1", vm.StringListItems[1].Text);
    Assert.Equal("crc32", vm.StringListItems[2].Text);
}

[Fact]
public void StringList_ModifyItem_SyncsToValue()
{
    var option = new RcloneOption { Name = "hashes", Type = "CommaSepList" };
    var vm = new MountOptionInputViewModel(option, "md5,sha1");

    vm.StringListItems[0].Text = "crc32";

    Assert.Equal("crc32,sha1", vm.Value);
}

[Fact]
public void StringList_AddItem_AppearsInCollection()
{
    var option = new RcloneOption { Name = "hashes", Type = "CommaSepList" };
    var vm = new MountOptionInputViewModel(option);

    vm.AddStringListItemCommand.Execute(null);

    Assert.Single(vm.StringListItems);
}

[Fact]
public void StringList_RemoveItem_SyncsToValue()
{
    var option = new RcloneOption { Name = "hashes", Type = "CommaSepList" };
    var vm = new MountOptionInputViewModel(option, "md5,sha1,crc32");

    vm.StringListItems[1].RemoveCommand.Execute(null);

    Assert.Equal(2, vm.StringListItems.Count);
    Assert.Equal("md5,crc32", vm.Value);
}

[Fact]
public void StringList_SpaceSeparated_UsesSpaceSeparator()
{
    var option = new RcloneOption { Name = "ciphers", Type = "SpaceSepList" };
    var vm = new MountOptionInputViewModel(option, "aes128-ctr aes256-ctr");

    Assert.Equal(2, vm.StringListItems.Count);
    vm.StringListItems[0].Text = "chacha20";
    Assert.Equal("chacha20 aes256-ctr", vm.Value);
}

[Fact]
public void StringList_KeyValue_HeadersParsedCorrectly()
{
    var option = new RcloneOption { Name = "headers", Type = "CommaSepList" };
    var vm = new MountOptionInputViewModel(option, "X-Foo: bar,X-Baz: qux");

    Assert.Equal(2, vm.StringListItems.Count);
    Assert.True(vm.StringListItems[0].IsKeyValue);
    Assert.Equal("X-Foo", vm.StringListItems[0].Key);
    Assert.Equal("bar", vm.StringListItems[0].ItemValue);
    Assert.Equal("X-Baz", vm.StringListItems[1].Key);
    Assert.Equal("qux", vm.StringListItems[1].ItemValue);
}

[Fact]
public void StringList_KeyValue_SerializesCorrectly()
{
    var option = new RcloneOption { Name = "headers", Type = "CommaSepList" };
    var vm = new MountOptionInputViewModel(option);

    vm.AddStringListItemCommand.Execute(null);
    vm.StringListItems[0].Key = "Authorization";
    vm.StringListItems[0].ItemValue = "Bearer token123";

    Assert.Equal("Authorization: Bearer token123", vm.Value);
}

[Fact]
public void StringList_ResetToDefault_ClearsItems()
{
    var option = new RcloneOption { Name = "hashes", Type = "CommaSepList" };
    var vm = new MountOptionInputViewModel(option, "md5,sha1");

    vm.ResetToDefaultCommand.Execute(null);

    Assert.Empty(vm.StringListItems);
    Assert.Equal(string.Empty, vm.Value);
}
```

**Step 7: Run tests**

Run: `dotnet test`
Expected: All tests pass

**Step 8: Commit**

```
feat: add StringListItemViewModel and integrate StringList into TypedOptionViewModel
```

---

### Task 3: XAML template and template selector wiring

**Files:**
- Modify: `RcloneMountManager.GUI/App.axaml`
- Modify: `RcloneMountManager.GUI/Controls/OptionControlTemplateSelector.cs`
- Modify: `RcloneMountManager.GUI/Controls/OptionTemplateSelectorFactory.cs`

**Step 1: Add StringList to `OptionControlTemplateSelector`**

In the switch in `Build()`, add before `_ => "Text"`:
```csharp
Core.Models.OptionControlType.StringList => "StringList",
```

**Step 2: Add "StringList" to `OptionTemplateSelectorFactory.Keys`**

Change line 10 from:
```csharp
private static readonly string[] Keys = ["Toggle", "ComboBox", "Numeric", "Duration", "SizeSuffix", "Text"];
```
to:
```csharp
private static readonly string[] Keys = ["Toggle", "ComboBox", "Numeric", "Duration", "SizeSuffix", "StringList", "Text"];
```

**Step 3: Add StringList DataTemplate to App.axaml**

Add before the closing `</Application.Resources>` tag (but after the Text template), a new template.

The template has two modes: simple text items, and key-value items (for headers). We use `IsKeyValue` on the parent ViewModel to decide. The items come from `StringListItems` collection.

```xml
<DataTemplate x:Key="StringList" x:CompileBindings="False">
    <Grid ColumnDefinitions="200,*,Auto" RowDefinitions="Auto,Auto,Auto" Margin="0,2">
            <TextBlock Grid.Row="0" Grid.Column="0"
                       Text="{Binding Label}"
                       TextTrimming="CharacterEllipsis"
                       VerticalAlignment="Top"
                       FontSize="12"
                       Margin="0,6,0,0"
                       FontWeight="{Binding HasNonDefaultValue, Converter={StaticResource ModifiedFontWeightConverter}}"/>
            <StackPanel Grid.Row="0" Grid.Column="1" Margin="8,0,0,0">
                <ItemsControl ItemsSource="{Binding StringListItems}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate x:CompileBindings="False">
                            <Grid Margin="0,1">
                                <Grid ColumnDefinitions="*,Auto" IsVisible="{Binding !IsKeyValue}">
                                    <TextBox Grid.Column="0" Text="{Binding Text}" Watermark="Value" HorizontalAlignment="Stretch"/>
                                    <Button Grid.Column="1" Command="{Binding RemoveCommand}" Content="X" Padding="6,4" Margin="4,0,0,0" FontSize="11"/>
                                </Grid>
                                <Grid ColumnDefinitions="*,*,Auto" IsVisible="{Binding IsKeyValue}">
                                    <TextBox Grid.Column="0" Text="{Binding Key}" Watermark="Key" HorizontalAlignment="Stretch"/>
                                    <TextBox Grid.Column="1" Text="{Binding ItemValue}" Watermark="Value" HorizontalAlignment="Stretch" Margin="4,0,0,0"/>
                                    <Button Grid.Column="2" Command="{Binding RemoveCommand}" Content="X" Padding="6,4" Margin="4,0,0,0" FontSize="11"/>
                                </Grid>
                            </Grid>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <Button Content="+" Command="{Binding AddStringListItemCommand}" Padding="6,2" FontSize="11" HorizontalAlignment="Left" Margin="0,2,0,0"/>
            </StackPanel>
            <Button Grid.Row="0" Grid.Column="2"
                    Command="{Binding ResetToDefaultCommand}"
                    Padding="6,4" Margin="4,6,0,0"
                    VerticalAlignment="Top"
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
</DataTemplate>
```

**Step 4: Build and verify**

Run: `dotnet build`
Expected: 0 warnings, 0 errors

Run: `dotnet test`
Expected: All tests pass

**Step 5: Commit**

```
feat: add StringList XAML template and wire into template selector
```

---

### Task 4: Handle `CommaSepList` in `RcloneBackendOption.GetControlType()`

**Files:**
- Modify: `RcloneMountManager.Core/Models/RcloneBackendOption.cs`

This is important: the backend option `GetControlType()` currently falls through to ComboBox for options that have `Examples`. But `CommaSepList` with examples (like `access_scopes`) should be `StringList`, not `ComboBox`. The type check for StringList must happen BEFORE the Examples/ComboBox fallback.

**Step 1: Update `GetControlType()`**

The current logic:
```csharp
public OptionControlType GetControlType()
{
    if (IsPassword) return OptionControlType.Text;
    var typeControl = Type switch
    {
        "bool" => OptionControlType.Toggle,
        "int" or "int64" or "uint32" or "float64" => OptionControlType.Numeric,
        "Duration" => OptionControlType.Duration,
        "SizeSuffix" => OptionControlType.SizeSuffix,
        _ => OptionControlType.Text,
    };

    if (typeControl is not OptionControlType.Text) return typeControl;
    if (GetEnumValues() is not null) return OptionControlType.ComboBox;
    return OptionControlType.Text;
}
```

Change the switch to include StringList types:
```csharp
public OptionControlType GetControlType()
{
    if (IsPassword) return OptionControlType.Text;
    var typeControl = Type switch
    {
        "bool" => OptionControlType.Toggle,
        "int" or "int64" or "uint32" or "float64" => OptionControlType.Numeric,
        "Duration" => OptionControlType.Duration,
        "SizeSuffix" => OptionControlType.SizeSuffix,
        "CommaSepList" or "SpaceSepList" or "stringArray" => OptionControlType.StringList,
        _ => OptionControlType.Text,
    };

    if (typeControl is not OptionControlType.Text) return typeControl;
    if (GetEnumValues() is not null) return OptionControlType.ComboBox;
    return OptionControlType.Text;
}
```

**Step 2: Run tests**

Run: `dotnet test`
Expected: All pass

**Step 3: Commit**

```
fix: detect StringList types before ComboBox fallback in backend options
```

---

### Task 5: Deduplicate backend options + final verification

**Files:**
- Modify: `RcloneMountManager.Core/Services/RcloneBackendService.cs`

**Step 1: Deduplicate options by name**

In `GetBackendsAsync()`, after the `.Where(o => !string.IsNullOrWhiteSpace(o.Name))` on line 56, add `.DistinctBy(o => o.Name)`:

Change:
```csharp
.Where(o => !string.IsNullOrWhiteSpace(o.Name))
.ToList(),
```
to:
```csharp
.Where(o => !string.IsNullOrWhiteSpace(o.Name))
.DistinctBy(o => o.Name)
.ToList(),
```

**Step 2: Build and test**

Run: `dotnet build`
Expected: 0 warnings, 0 errors

Run: `dotnet test`
Expected: All pass

**Step 3: Commit**

```
fix: deduplicate backend options by name
```

---

### Task 6: Final integration test

**Step 1: Run full build**

Run: `dotnet build`
Expected: 0 warnings, 0 errors

**Step 2: Run all tests**

Run: `dotnet test`
Expected: All pass (67 existing + new tests)

**Step 3: Verify git log is clean**

Run: `git status`
Expected: Clean tree
