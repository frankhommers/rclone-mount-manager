using RcloneMountManager.Core.Models;
using RcloneMountManager.ViewModels;
using System.Reflection;

namespace RcloneMountManager.Tests.ViewModels;

public class MountOptionsViewModelTests
{
    [Fact]
    public void GetPinnedOptionNames_ReturnsOnlyPinnedOptions()
    {
        var viewModel = CreateViewModelWithGroups();

        var transfers = viewModel.Groups[0].AllOptions[0];
        var cacheMode = viewModel.Groups[0].AllOptions[1];

        transfers.IsPinned = true;
        cacheMode.IsPinned = false;

        var pinned = viewModel.GetPinnedOptionNames();

        Assert.Single(pinned);
        Assert.Contains("transfers", pinned);
    }

    [Fact]
    public void UpdateFromProfile_RestoresPinnedStateFromProfile()
    {
        var viewModel = CreateViewModelWithGroups();

        var values = new Dictionary<string, string>
        {
            ["transfers"] = "8",
        };

        var pinnedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "transfers",
        };

        viewModel.UpdateFromProfile(values, pinnedNames);

        var transfers = viewModel.Groups[0].AllOptions.Single(o => o.Name == "transfers");
        var cacheMode = viewModel.Groups[0].AllOptions.Single(o => o.Name == "vfs_cache_mode");

        Assert.True(transfers.IsPinned);
        Assert.Equal("8", transfers.Value);
        Assert.False(cacheMode.IsPinned);
    }

    private static MountOptionsViewModel CreateViewModelWithGroups()
    {
        var viewModel = new MountOptionsViewModel();
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
                    new RcloneOption { Name = "transfers", Type = "int" },
                    new RcloneOption { Name = "vfs_cache_mode", Type = "string" },
                ],
            },
        ];
    }

    private static void SetAllGroups(MountOptionsViewModel viewModel, IReadOnlyList<RcloneOptionGroup> groups)
    {
        var field = typeof(MountOptionsViewModel).GetField("_allGroups", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(viewModel, groups);
    }
}
