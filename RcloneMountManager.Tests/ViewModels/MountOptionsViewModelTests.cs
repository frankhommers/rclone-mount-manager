using RcloneMountManager.Core.Models;
using System.Reflection;
using RcloneMountManager.GUI.ViewModels;

namespace RcloneMountManager.Tests.ViewModels;

public class MountOptionsViewModelTests
{
  [Theory]
  [InlineData("vfs_cache_mode")]
  [InlineData("--VFS-CACHE-MODE")]
  [InlineData("vfs cache mode")]
  [InlineData("disk caching")]
  public void Search_FiltersAndExpandsMatchingGroups(string search)
  {
    MountOptionsViewModel viewModel = CreateViewModelWithGroups();
    viewModel.Groups.Add(new MountOptionGroupViewModel
    {
      Name = "rc",
      AllOptions = [new MountOptionInputViewModel(new RcloneOption { Name = "rc_addr" })],
    });

    viewModel.SearchText = search;

    Assert.Equal("vfs_cache_mode", Assert.Single(viewModel.Groups[0].VisibleOptions).Name);
    Assert.True(viewModel.Groups[0].IsEffectivelyExpanded);
    Assert.False(viewModel.Groups[1].HasVisibleOptions);
    Assert.False(viewModel.HasNoSearchResults);
  }

  [Fact]
  public void Search_IncludesAdvancedOptionsAndRestoresPreviousExpansion()
  {
    MountOptionsViewModel viewModel = CreateViewModelWithGroups();
    viewModel.ShowAdvancedOptions = false;
    Assert.DoesNotContain(viewModel.Groups[0].VisibleOptions, option => option.Name == "vfs_cache_mode");

    viewModel.SearchText = "cache";

    Assert.Equal("vfs_cache_mode", Assert.Single(viewModel.Groups[0].VisibleOptions).Name);
    Assert.True(viewModel.Groups[0].IsEffectivelyExpanded);
    Assert.False(viewModel.ShowAdvancedOptions);

    viewModel.ClearSearchCommand.Execute(null);

    Assert.False(viewModel.Groups[0].IsEffectivelyExpanded);
    Assert.Equal("transfers", Assert.Single(viewModel.Groups[0].VisibleOptions).Name);
  }

  [Fact]
  public void Search_PreservesHiddenValuesAndPinsWhenSaving()
  {
    MountOptionsViewModel viewModel = CreateViewModelWithGroups();
    MountOptionInputViewModel transfers = viewModel.Groups[0].AllOptions[0];
    transfers.Value = "8";
    transfers.IsPinned = true;

    viewModel.SearchText = "no-such-option";

    Assert.True(viewModel.HasNoSearchResults);
    Assert.Equal("8", viewModel.GetNonDefaultValues()["transfers"]);
    Assert.Contains("transfers", viewModel.GetPinnedOptionNames());
    Assert.Equal(["--transfers", "8"], viewModel.ToCommandLineArguments());

    viewModel.ClearSearchCommand.Execute(null);

    Assert.False(viewModel.HasNoSearchResults);
    Assert.Same(transfers, viewModel.Groups[0].VisibleOptions.First());
    Assert.Equal("8", transfers.Value);
  }

  [Fact]
  public void GetPinnedOptionNames_ReturnsOnlyPinnedOptions()
  {
    MountOptionsViewModel viewModel = CreateViewModelWithGroups();

    MountOptionInputViewModel transfers = viewModel.Groups[0].AllOptions[0];
    MountOptionInputViewModel cacheMode = viewModel.Groups[0].AllOptions[1];

    transfers.IsPinned = true;
    cacheMode.IsPinned = false;

    HashSet<string> pinned = viewModel.GetPinnedOptionNames();

    Assert.Single(pinned);
    Assert.Contains("transfers", pinned);
  }

  [Fact]
  public void UpdateFromProfile_RestoresPinnedStateFromProfile()
  {
    MountOptionsViewModel viewModel = CreateViewModelWithGroups();

    Dictionary<string, string> values = new()
    {
      ["transfers"] = "8",
    };

    HashSet<string> pinnedNames = new(StringComparer.OrdinalIgnoreCase)
    {
      "transfers",
    };

    viewModel.UpdateFromProfile(values, pinnedNames);

    MountOptionInputViewModel transfers = viewModel.Groups[0].AllOptions.Single(o => o.Name == "transfers");
    MountOptionInputViewModel cacheMode = viewModel.Groups[0].AllOptions.Single(o => o.Name == "vfs_cache_mode");

    Assert.True(transfers.IsPinned);
    Assert.Equal("8", transfers.Value);
    Assert.False(cacheMode.IsPinned);
  }

  private static MountOptionsViewModel CreateViewModelWithGroups()
  {
    MountOptionsViewModel viewModel = new();
    SetAllGroups(viewModel, CreateOptionGroups());
    viewModel.UpdateFromProfile(new Dictionary<string, string>());
    return viewModel;
  }

  private static IReadOnlyList<RcloneOptionGroup> CreateOptionGroups()
  {
    return
    [
      new RcloneOptionGroup
      {
        Name = "vfs",
        DisplayName = "VFS",
        Options =
        [
          new RcloneOption {Name = "transfers", Type = "int"},
          new RcloneOption {Name = "vfs_cache_mode", Type = "string", Advanced = true, Help = "Disk caching mode"},
        ],
      },
    ];
  }

  private static void SetAllGroups(MountOptionsViewModel viewModel, IReadOnlyList<RcloneOptionGroup> groups)
  {
    FieldInfo? field = typeof(MountOptionsViewModel).GetField(
      "_allGroups",
      BindingFlags.Instance | BindingFlags.NonPublic);
    Assert.NotNull(field);
    field.SetValue(viewModel, groups);
  }
}
