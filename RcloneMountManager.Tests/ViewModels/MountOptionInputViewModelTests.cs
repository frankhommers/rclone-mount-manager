using RcloneMountManager.Core.Helpers;
using RcloneMountManager.Core.Models;
using RcloneMountManager.GUI.ViewModels;

namespace RcloneMountManager.Tests.ViewModels;

public class MountOptionInputViewModelTests
{
  [Fact]
  public void StringList_ControlType_DetectedForStringArray()
  {
    RcloneOption option = new() {Name = "exclude", Type = "stringArray"};
    MountOptionInputViewModel vm = new(option);

    Assert.Equal(OptionControlType.StringList, vm.ControlType);
  }

  [Fact]
  public void StringList_InitWithCommaSeparatedValue_PopulatesItems()
  {
    RcloneOption option = new() {Name = "exclude", Type = "CommaSepList"};
    MountOptionInputViewModel vm = new(option, "*.tmp,*.bak,*.log");

    Assert.Equal(3, vm.StringListItems.Count);
    Assert.Equal("*.tmp", vm.StringListItems[0].Text);
    Assert.Equal("*.bak", vm.StringListItems[1].Text);
    Assert.Equal("*.log", vm.StringListItems[2].Text);
  }

  [Fact]
  public void StringList_ModifyItem_SyncsToValue()
  {
    RcloneOption option = new() {Name = "exclude", Type = "CommaSepList"};
    MountOptionInputViewModel vm = new(option, "*.tmp,*.bak");

    vm.StringListItems[1].Text = "*.cache";

    Assert.Equal("*.tmp,*.cache", vm.Value);
  }

  [Fact]
  public void StringList_AddItem_AppearsInCollection()
  {
    RcloneOption option = new() {Name = "exclude", Type = "CommaSepList"};
    MountOptionInputViewModel vm = new(option);

    vm.AddStringListItemCommand.Execute(null);

    Assert.Single(vm.StringListItems);
  }

  [Fact]
  public void StringList_RemoveItem_SyncsToValue()
  {
    RcloneOption option = new() {Name = "exclude", Type = "CommaSepList"};
    MountOptionInputViewModel vm = new(option, "*.tmp,*.bak");

    vm.StringListItems[0].RemoveCommand.Execute(null);

    Assert.Single(vm.StringListItems);
    Assert.Equal("*.bak", vm.Value);
  }

  [Fact]
  public void StringList_SpaceSeparated_UsesSpaceSeparator()
  {
    RcloneOption option = new() {Name = "include", Type = "SpaceSepList"};
    MountOptionInputViewModel vm = new(option, "one two");

    vm.AddStringListItemCommand.Execute(null);
    vm.StringListItems[2].Text = "three";

    Assert.Equal("one two three", vm.Value);
  }

  [Fact]
  public void StringList_KeyValueHeaders_ParsedCorrectly()
  {
    RcloneOption option = new() {Name = "headers", Type = "CommaSepList"};
    MountOptionInputViewModel vm = new(option, "Accept: application/json,Cache-Control: no-cache");

    Assert.True(vm.IsKeyValue);
    Assert.Equal("Accept", vm.StringListItems[0].Key);
    Assert.Equal("application/json", vm.StringListItems[0].ItemValue);
    Assert.Equal("Cache-Control", vm.StringListItems[1].Key);
    Assert.Equal("no-cache", vm.StringListItems[1].ItemValue);
  }

  [Fact]
  public void StringList_KeyValueHeaders_SerializesCorrectly()
  {
    RcloneOption option = new() {Name = "headers", Type = "CommaSepList"};
    MountOptionInputViewModel vm = new(option);

    vm.AddStringListItemCommand.Execute(null);
    vm.StringListItems[0].Key = "Accept";
    vm.StringListItems[0].ItemValue = "application/json";

    Assert.Equal("Accept: application/json", vm.Value);
  }

  [Fact]
  public void StringList_Reset_ClearsItems()
  {
    RcloneOption option = new() {Name = "exclude", Type = "CommaSepList"};
    MountOptionInputViewModel vm = new(option, "*.tmp,*.bak");

    vm.ResetToDefaultCommand.Execute(null);

    Assert.Empty(vm.StringListItems);
    Assert.Equal(string.Empty, vm.Value);
  }

  [Fact]
  public void Toggle_BoolValue_SyncsToValueString()
  {
    RcloneOption option = new() {Name = "debug_fuse", Type = "bool"};
    MountOptionInputViewModel vm = new(option);

    vm.BoolValue = true;

    Assert.Equal("true", vm.Value);
    Assert.True(vm.IsSet);
  }

  [Fact]
  public void Toggle_InitWithTrue_SetsBoolValue()
  {
    RcloneOption option = new() {Name = "debug_fuse", Type = "bool"};
    MountOptionInputViewModel vm = new(option, "true");

    Assert.True(vm.BoolValue);
    Assert.True(vm.IsSet);
  }

  [Fact]
  public void Toggle_InitWithFalse_BoolValueIsFalse()
  {
    RcloneOption option = new() {Name = "debug_fuse", Type = "bool"};
    MountOptionInputViewModel vm = new(option, "false");

    Assert.False(vm.BoolValue);
  }

  [Fact]
  public void Numeric_NumericValue_SyncsToValueString()
  {
    RcloneOption option = new() {Name = "transfers", Type = "int"};
    MountOptionInputViewModel vm = new(option);

    vm.NumericValue = 8;

    Assert.Equal("8", vm.Value);
    Assert.True(vm.IsSet);
  }

  [Fact]
  public void Numeric_InitWithValue_SetsNumericValue()
  {
    RcloneOption option = new() {Name = "transfers", Type = "int"};
    MountOptionInputViewModel vm = new(option, "16");

    Assert.Equal(16m, vm.NumericValue);
  }

  [Fact]
  public void Duration_DurationValue_SyncsToValueString()
  {
    RcloneOption option = new() {Name = "dir_cache_time", Type = "Duration"};
    MountOptionInputViewModel vm = new(option);

    vm.DurationValue = new TimeSpan(1, 30, 0);

    Assert.Equal("1h30m", vm.Value);
  }

  [Fact]
  public void Duration_InitWithString_SetsDurationValue()
  {
    RcloneOption option = new() {Name = "dir_cache_time", Type = "Duration"};
    MountOptionInputViewModel vm = new(option, "5m30s");

    Assert.Equal(new TimeSpan(0, 5, 30), vm.DurationValue);
  }

  [Fact]
  public void SizeSuffix_Components_SyncToValueString()
  {
    RcloneOption option = new() {Name = "buffer_size", Type = "SizeSuffix"};
    MountOptionInputViewModel vm = new(option);

    vm.SizeSuffixNumericValue = 128;
    vm.SizeSuffixUnit = SizeSuffixHelper.UnitItems.First(u => u.Value == "Mi");

    Assert.Equal("128Mi", vm.Value);
  }

  [Fact]
  public void SizeSuffix_InitWithString_SetsComponents()
  {
    RcloneOption option = new() {Name = "buffer_size", Type = "SizeSuffix"};
    MountOptionInputViewModel vm = new(option, "256Gi");

    Assert.Equal(256m, vm.SizeSuffixNumericValue);
    Assert.Equal("Gi", vm.SizeSuffixUnit.Value);
  }

  [Fact]
  public void ComboBox_SelectedEnumValue_SyncsToValueString()
  {
    RcloneOption option = new() {Name = "vfs_cache_mode", Type = "CacheMode"};
    MountOptionInputViewModel vm = new(option);

    vm.SelectedEnumValue = "full";

    Assert.Equal("full", vm.Value);
  }

  [Fact]
  public void ComboBox_InitWithString_SetsSelectedEnumValue()
  {
    RcloneOption option = new() {Name = "vfs_cache_mode", Type = "CacheMode"};
    MountOptionInputViewModel vm = new(option, "writes");

    Assert.Equal("writes", vm.SelectedEnumValue);
  }

  [Fact]
  public void ResetToDefault_ClearsValueAndIsSet()
  {
    RcloneOption option = new() {Name = "transfers", Type = "int"};
    MountOptionInputViewModel vm = new(option, "8");

    vm.ResetToDefaultCommand.Execute(null);

    Assert.Equal(string.Empty, vm.Value);
    Assert.False(vm.IsSet);
    Assert.False(vm.HasNonDefaultValue);
  }

  [Fact]
  public void Text_Value_WorksDirectly()
  {
    RcloneOption option = new() {Name = "include", Type = "string"};
    MountOptionInputViewModel vm = new(option);

    vm.Value = "*.txt";

    Assert.Equal("*.txt", vm.Value);
    Assert.True(vm.IsSet);
  }

  [Fact]
  public void IsPinned_DefaultFalse()
  {
    RcloneOption option = new() {Name = "transfers", Type = "int"};
    MountOptionInputViewModel vm = new(option);
    Assert.False(vm.IsPinned);
  }

  [Fact]
  public void IsPinned_AutoPinsOnNonDefaultValue()
  {
    RcloneOption option = new() {Name = "transfers", Type = "int", DefaultStr = "4"};
    MountOptionInputViewModel vm = new(option);
    vm.NumericValue = 8;
    Assert.True(vm.IsPinned);
  }

  [Fact]
  public void IsPinned_ResetUnpins()
  {
    RcloneOption option = new() {Name = "transfers", Type = "int", DefaultStr = "4"};
    MountOptionInputViewModel vm = new(option);
    vm.NumericValue = 8;
    Assert.True(vm.IsPinned);
    vm.ResetToDefaultCommand.Execute(null);
    Assert.False(vm.IsPinned);
  }

  [Fact]
  public void ShouldInclude_FalseByDefault()
  {
    RcloneOption option = new() {Name = "transfers", Type = "int"};
    MountOptionInputViewModel vm = new(option);
    Assert.False(vm.ShouldInclude);
  }

  [Fact]
  public void ShouldInclude_TrueWhenNonDefault()
  {
    RcloneOption option = new() {Name = "transfers", Type = "int", DefaultStr = "4"};
    MountOptionInputViewModel vm = new(option);
    vm.NumericValue = 8;
    Assert.True(vm.ShouldInclude);
  }

  [Fact]
  public void ShouldInclude_TrueWhenPinnedAtDefault()
  {
    RcloneOption option = new() {Name = "vfs_cache_mode", Type = "string", DefaultStr = "off"};
    MountOptionInputViewModel vm = new(option);
    vm.IsPinned = true;
    vm.Value = "off";
    Assert.True(vm.ShouldInclude);
  }

  [Fact]
  public void ShouldInclude_FalseWhenUnpinnedAtDefault()
  {
    RcloneOption option = new() {Name = "vfs_cache_mode", Type = "string", DefaultStr = "off"};
    MountOptionInputViewModel vm = new(option);
    vm.Value = "off";
    vm.IsPinned = false;
    Assert.False(vm.ShouldInclude);
  }

  [Fact]
  public void IsPassword_True_ReflectsOptionMetadata()
  {
    RcloneOption option = new() {Name = "rc_pass", Type = "string", IsPassword = true};
    MountOptionInputViewModel vm = new(option);

    Assert.True(vm.IsPassword);
  }

  [Fact]
  public void ShouldInclude_IsPasswordFalse_UnaffectedByConfirmValue()
  {
    RcloneOption option = new() {Name = "include", Type = "string", DefaultStr = ""};
    MountOptionInputViewModel vm = new(option);

    vm.Value = "*.txt";
    vm.ConfirmValue = "different";

    Assert.True(vm.ShouldInclude);
    Assert.False(vm.HasSecretMismatch);
  }

  [Fact]
  public void ShouldInclude_IsPasswordTrue_FalseWhenConfirmValueMismatches()
  {
    RcloneOption option = new() {Name = "rc_pass", Type = "string", IsPassword = true};
    MountOptionInputViewModel vm = new(option);

    vm.Value = "topsecret";
    vm.ConfirmValue = "different";

    Assert.True(vm.HasSecretMismatch);
    Assert.False(vm.ShouldInclude);
  }

  [Fact]
  public void ShouldInclude_IsPasswordTrue_TrueWhenConfirmValueMatches()
  {
    RcloneOption option = new() {Name = "rc_pass", Type = "string", IsPassword = true};
    MountOptionInputViewModel vm = new(option);

    vm.Value = "topsecret";
    vm.ConfirmValue = "topsecret";

    Assert.False(vm.HasSecretMismatch);
    Assert.True(vm.ShouldInclude);
  }
}