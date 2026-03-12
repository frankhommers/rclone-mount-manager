using RcloneMountManager.Core.Models;
using System.Reflection;
using RcloneMountManager.GUI.ViewModels;

namespace RcloneMountManager.Tests.ViewModels;

public class MountOptionsViewModelTests
{
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
          new RcloneOption {Name = "vfs_cache_mode", Type = "string"},
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