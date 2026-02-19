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
}
