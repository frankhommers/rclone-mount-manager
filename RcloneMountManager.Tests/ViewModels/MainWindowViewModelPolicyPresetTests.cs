using RcloneMountManager.Core.Models;
using RcloneMountManager.ViewModels;
using System.Text.Json;
using System.Reflection;

namespace RcloneMountManager.Tests.ViewModels;

public sealed class MainWindowViewModelPolicyPresetTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"main-window-policy-preset-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ApplyReliabilityPreset_WritesManagedReliabilityKeys()
    {
        var viewModel = CreateViewModel(CreateProfilesPath());
        var preset = ReliabilityPolicyPreset.GetByIdOrDefault(ReliabilityPolicyPreset.UnreliableId);

        SeedMountOptions(viewModel, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["custom_header"] = "keep-me",
        });

        viewModel.SelectedReliabilityPresetId = preset.Id;
        viewModel.ApplyReliabilityPresetCommand.Execute(null);

        Assert.Equal(preset.Id, viewModel.SelectedProfile.SelectedReliabilityPresetId);
        foreach (var key in ReliabilityPolicyPreset.ManagedReliabilityKeys)
        {
            Assert.Equal(preset.OptionOverrides[key], viewModel.SelectedProfile.MountOptions[key]);
        }
    }

    [Fact]
    public void ApplyReliabilityPreset_PreservesUnrelatedMountOptionKeys()
    {
        var viewModel = CreateViewModel(CreateProfilesPath());
        var preset = ReliabilityPolicyPreset.GetByIdOrDefault(ReliabilityPolicyPreset.StableId);
        const string customKey = "custom_flag";
        const string customValue = "true";
        const string rcAddrKey = "rc_addr";
        const string rcAddrValue = "localhost:50123";

        SeedMountOptions(viewModel, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [customKey] = customValue,
            [rcAddrKey] = rcAddrValue,
            ["vfs_cache_mode"] = "off",
        });

        viewModel.SelectedReliabilityPresetId = preset.Id;
        viewModel.ApplyReliabilityPresetCommand.Execute(null);

        Assert.Equal(customValue, viewModel.SelectedProfile.MountOptions[customKey]);
        Assert.Equal(rcAddrValue, viewModel.SelectedProfile.MountOptions[rcAddrKey]);
        Assert.Equal(preset.OptionOverrides["vfs_cache_mode"], viewModel.SelectedProfile.MountOptions["vfs_cache_mode"]);
    }

    [Fact]
    public void SaveChanges_PersistsSelectedReliabilityPresetIdToProfilesJson()
    {
        var profilesPath = CreateProfilesPath();
        var viewModel = CreateViewModel(profilesPath);
        var preset = ReliabilityPolicyPreset.GetByIdOrDefault(ReliabilityPolicyPreset.StableId);

        viewModel.SelectedReliabilityPresetId = preset.Id;
        viewModel.ApplyReliabilityPresetCommand.Execute(null);
        viewModel.SaveChangesCommand.Execute(null);

        var persistedPresetId = ReadPersistedSelectedPresetId(profilesPath, viewModel.SelectedProfile.Id);
        Assert.Equal(preset.Id, persistedPresetId);
    }

    [Fact]
    public void LoadingProfiles_RestoresSelectedPresetIdAndEffectiveManagedOptions()
    {
        var profilesPath = CreateProfilesPath();
        var preset = ReliabilityPolicyPreset.GetByIdOrDefault(ReliabilityPolicyPreset.UnreliableId);

        var saveViewModel = CreateViewModel(profilesPath);
        SeedMountOptions(saveViewModel, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["custom_header"] = "keep-me",
            ["retries"] = "1",
        });

        saveViewModel.SelectedReliabilityPresetId = preset.Id;
        saveViewModel.ApplyReliabilityPresetCommand.Execute(null);
        saveViewModel.SaveChangesCommand.Execute(null);

        var reloadViewModel = CreateViewModel(profilesPath);

        Assert.Equal(preset.Id, reloadViewModel.SelectedReliabilityPresetId);
        Assert.Equal(preset.Id, reloadViewModel.SelectedProfile.SelectedReliabilityPresetId);
        Assert.Equal("keep-me", reloadViewModel.SelectedProfile.MountOptions["custom_header"]);
        foreach (var (key, value) in preset.OptionOverrides)
        {
            Assert.Equal(value, reloadViewModel.SelectedProfile.MountOptions[key]);
        }
    }

    private MainWindowViewModel CreateViewModel(string profilesPath)
        => new(
            profilesFilePath: profilesPath,
            startupEnabledProbe: _ => false,
            loadStartupData: false);

    private string CreateProfilesPath()
    {
        Directory.CreateDirectory(_tempRoot);
        return Path.Combine(_tempRoot, "profiles.json");
    }

    private static void SeedMountOptions(MainWindowViewModel viewModel, Dictionary<string, string> mountOptions)
    {
        var seededOptions = new Dictionary<string, string>(mountOptions, StringComparer.OrdinalIgnoreCase);
        viewModel.SelectedProfile.MountOptions = seededOptions;

        PrimeMountOptionsVm(viewModel.MountOptionsVm, seededOptions.Keys.Concat(ReliabilityPolicyPreset.ManagedReliabilityKeys));
        viewModel.MountOptionsVm.UpdateFromProfile(seededOptions, viewModel.SelectedProfile.PinnedMountOptions);
    }

    private static void PrimeMountOptionsVm(MountOptionsViewModel mountOptionsVm, IEnumerable<string> optionNames)
    {
        var allGroupsField = typeof(MountOptionsViewModel)
            .GetField("_allGroups", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not access MountOptionsViewModel._allGroups for test setup.");

        var options = optionNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => new RcloneOption
            {
                Name = name,
                Type = "string",
                DefaultStr = string.Empty,
            })
            .ToList();

        allGroupsField.SetValue(mountOptionsVm, new List<RcloneOptionGroup>
        {
            new()
            {
                Name = "mount",
                DisplayName = "Mount",
                Options = options,
            },
        });
    }

    private static string ReadPersistedSelectedPresetId(string profilesPath, string profileId)
    {
        var json = File.ReadAllText(profilesPath);
        using var document = JsonDocument.Parse(json);
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            var id = entry.GetProperty("Id").GetString();
            if (!string.Equals(id, profileId, StringComparison.Ordinal))
            {
                continue;
            }

            return entry.GetProperty("SelectedReliabilityPresetId").GetString()
                ?? throw new InvalidOperationException("SelectedReliabilityPresetId is null in persisted profile.");
        }

        throw new InvalidOperationException($"Persisted profile '{profileId}' was not found.");
    }
}
