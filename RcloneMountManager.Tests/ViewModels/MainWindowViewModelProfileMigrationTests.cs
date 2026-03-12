using RcloneMountManager.Core.Models;
using System.Text.Json;
using RcloneMountManager.GUI.ViewModels;

namespace RcloneMountManager.Tests.ViewModels;

public sealed class MainWindowViewModelProfileMigrationTests : IDisposable
{
  private readonly string _tempRoot = Path.Combine(
    Path.GetTempPath(),
    $"main-window-profile-migration-tests-{Guid.NewGuid():N}");

  public void Dispose()
  {
    if (Directory.Exists(_tempRoot))
    {
      Directory.Delete(_tempRoot, true);
    }
  }

  [Fact]
  public void LoadProfiles_MigratesLegacyRcMountOptions_ToRcPortAndRemovesLegacyKeys()
  {
    string profilesPath = CreateProfilesPath();
    File.WriteAllText(
      profilesPath,
      JsonSerializer.Serialize(
        new[]
        {
          new
          {
            Id = "profile-legacy-rc",
            Name = "Legacy RC",
            Type = MountType.RcloneAuto,
            Source = "remote:media",
            MountPoint = "/tmp/legacy-rc",
            ExtraOptions = string.Empty,
            SelectedReliabilityPresetId = ReliabilityPolicyPreset.StableId,
            MountOptions = new Dictionary<string, string>
            {
              ["rc"] = "true",
              ["rc_addr"] = "localhost:53111",
              ["rc_no_auth"] = "true",
              ["vfs_cache_mode"] = "full",
            },
            PinnedMountOptions = Array.Empty<string>(),
            RcloneBinaryPath = "rclone",
            AllowInsecurePasswordsInScript = false,
            StartAtLogin = false,
            IsRemoteDefinition = false,
            BackendName = string.Empty,
            BackendOptions = new Dictionary<string, string>(),
            RcPort = 0,
            EnableRemoteControl = true,
          },
        }));

    MainWindowViewModel viewModel = new(
      profilesPath,
      startupEnabledProbe: _ => false,
      loadStartupData: false);

    MountProfile profile = Assert.Single(viewModel.Profiles, static p => !p.IsRemoteDefinition);

    Assert.Equal(53111, profile.RcPort);
    Assert.DoesNotContain("rc", profile.MountOptions.Keys, StringComparer.OrdinalIgnoreCase);
    Assert.DoesNotContain("rc_addr", profile.MountOptions.Keys, StringComparer.OrdinalIgnoreCase);
    Assert.DoesNotContain("rc_no_auth", profile.MountOptions.Keys, StringComparer.OrdinalIgnoreCase);
    Assert.Equal("full", profile.MountOptions["vfs_cache_mode"]);
  }

  private string CreateProfilesPath()
  {
    Directory.CreateDirectory(_tempRoot);
    return Path.Combine(_tempRoot, "profiles.json");
  }
}