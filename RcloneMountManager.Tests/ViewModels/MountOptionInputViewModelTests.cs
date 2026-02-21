using RcloneMountManager.Core.Models;
using RcloneMountManager.ViewModels;

namespace RcloneMountManager.Tests.ViewModels;

public class MountOptionInputViewModelTests
{
    [Fact]
    public void StringList_ControlType_DetectedForStringArray()
    {
        var option = new RcloneOption { Name = "exclude", Type = "stringArray" };
        var vm = new MountOptionInputViewModel(option);

        Assert.Equal(OptionControlType.StringList, vm.ControlType);
    }

    [Fact]
    public void StringList_InitWithCommaSeparatedValue_PopulatesItems()
    {
        var option = new RcloneOption { Name = "exclude", Type = "CommaSepList" };
        var vm = new MountOptionInputViewModel(option, "*.tmp,*.bak,*.log");

        Assert.Equal(3, vm.StringListItems.Count);
        Assert.Equal("*.tmp", vm.StringListItems[0].Text);
        Assert.Equal("*.bak", vm.StringListItems[1].Text);
        Assert.Equal("*.log", vm.StringListItems[2].Text);
    }

    [Fact]
    public void StringList_ModifyItem_SyncsToValue()
    {
        var option = new RcloneOption { Name = "exclude", Type = "CommaSepList" };
        var vm = new MountOptionInputViewModel(option, "*.tmp,*.bak");

        vm.StringListItems[1].Text = "*.cache";

        Assert.Equal("*.tmp,*.cache", vm.Value);
    }

    [Fact]
    public void StringList_AddItem_AppearsInCollection()
    {
        var option = new RcloneOption { Name = "exclude", Type = "CommaSepList" };
        var vm = new MountOptionInputViewModel(option);

        vm.AddStringListItemCommand.Execute(null);

        Assert.Single(vm.StringListItems);
    }

    [Fact]
    public void StringList_RemoveItem_SyncsToValue()
    {
        var option = new RcloneOption { Name = "exclude", Type = "CommaSepList" };
        var vm = new MountOptionInputViewModel(option, "*.tmp,*.bak");

        vm.StringListItems[0].RemoveCommand.Execute(null);

        Assert.Single(vm.StringListItems);
        Assert.Equal("*.bak", vm.Value);
    }

    [Fact]
    public void StringList_SpaceSeparated_UsesSpaceSeparator()
    {
        var option = new RcloneOption { Name = "include", Type = "SpaceSepList" };
        var vm = new MountOptionInputViewModel(option, "one two");

        vm.AddStringListItemCommand.Execute(null);
        vm.StringListItems[2].Text = "three";

        Assert.Equal("one two three", vm.Value);
    }

    [Fact]
    public void StringList_KeyValueHeaders_ParsedCorrectly()
    {
        var option = new RcloneOption { Name = "headers", Type = "CommaSepList" };
        var vm = new MountOptionInputViewModel(option, "Accept: application/json,Cache-Control: no-cache");

        Assert.True(vm.IsKeyValue);
        Assert.Equal("Accept", vm.StringListItems[0].Key);
        Assert.Equal("application/json", vm.StringListItems[0].ItemValue);
        Assert.Equal("Cache-Control", vm.StringListItems[1].Key);
        Assert.Equal("no-cache", vm.StringListItems[1].ItemValue);
    }

    [Fact]
    public void StringList_KeyValueHeaders_SerializesCorrectly()
    {
        var option = new RcloneOption { Name = "headers", Type = "CommaSepList" };
        var vm = new MountOptionInputViewModel(option);

        vm.AddStringListItemCommand.Execute(null);
        vm.StringListItems[0].Key = "Accept";
        vm.StringListItems[0].ItemValue = "application/json";

        Assert.Equal("Accept: application/json", vm.Value);
    }

    [Fact]
    public void StringList_Reset_ClearsItems()
    {
        var option = new RcloneOption { Name = "exclude", Type = "CommaSepList" };
        var vm = new MountOptionInputViewModel(option, "*.tmp,*.bak");

        vm.ResetToDefaultCommand.Execute(null);

        Assert.Empty(vm.StringListItems);
        Assert.Equal(string.Empty, vm.Value);
    }

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
    public void Toggle_InitWithFalse_BoolValueIsFalse()
    {
        var option = new RcloneOption { Name = "debug_fuse", Type = "bool" };
        var vm = new MountOptionInputViewModel(option, "false");

        Assert.False(vm.BoolValue);
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
    public void Numeric_InitWithValue_SetsNumericValue()
    {
        var option = new RcloneOption { Name = "transfers", Type = "int" };
        var vm = new MountOptionInputViewModel(option, "16");

        Assert.Equal(16m, vm.NumericValue);
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
    public void ComboBox_InitWithString_SetsSelectedEnumValue()
    {
        var option = new RcloneOption { Name = "vfs_cache_mode", Type = "CacheMode" };
        var vm = new MountOptionInputViewModel(option, "writes");

        Assert.Equal("writes", vm.SelectedEnumValue);
    }

    [Fact]
    public void ResetToDefault_ClearsValueAndIsSet()
    {
        var option = new RcloneOption { Name = "transfers", Type = "int" };
        var vm = new MountOptionInputViewModel(option, "8");

        vm.ResetToDefaultCommand.Execute(null);

        Assert.Equal(string.Empty, vm.Value);
        Assert.False(vm.IsSet);
        Assert.False(vm.HasNonDefaultValue);
    }

    [Fact]
    public void Text_Value_WorksDirectly()
    {
        var option = new RcloneOption { Name = "include", Type = "string" };
        var vm = new MountOptionInputViewModel(option);

        vm.Value = "*.txt";

        Assert.Equal("*.txt", vm.Value);
        Assert.True(vm.IsSet);
    }

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
        vm.IsPinned = true;
        vm.Value = "off";
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
}
