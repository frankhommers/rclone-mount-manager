using RcloneMountManager.Core.Models;
using System.Text.Json;
using RcloneMountManager.GUI.ViewModels;

namespace RcloneMountManager.Tests.ViewModels;

public sealed class MainWindowViewModelStartupTests : IDisposable
{
  private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"main-window-startup-tests-{Guid.NewGuid():N}");

  public void Dispose()
  {
    if (Directory.Exists(_tempRoot))
    {
      Directory.Delete(_tempRoot, true);
    }
  }

  [Fact]
  public async Task ToggleStartupCommand_BlocksEnableWhenCriticalPreflightFails()
  {
    string profilesPath = CreateProfilesPath();
    MountProfile profile = new() {Id = "profile-1"};
    StartupPreflightReport report = new StartupPreflightReport().AddCritical("binary", "binary missing");
    bool enableCalled = false;

    MainWindowViewModel viewModel = CreateViewModel(
      profilesPath,
      profile,
      (_, _) => Task.FromResult(report),
      (_, _, _) =>
      {
        enableCalled = true;
        return Task.CompletedTask;
      },
      (_, _) => Task.CompletedTask);

    await viewModel.ToggleStartupCommand.ExecuteAsync(null);

    Assert.False(enableCalled);
    Assert.False(viewModel.SelectedProfile.StartAtLogin);
    Assert.Contains("1 critical", viewModel.StartupPreflightSummary, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("blocked", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    Assert.False(File.Exists(profilesPath));
  }

  [Fact]
  public async Task ToggleStartupCommand_EnablesAndPersistsStartAtLoginAfterSuccessfulApply()
  {
    string profilesPath = CreateProfilesPath();
    MountProfile profile = new() {Id = "profile-2"};
    StartupPreflightReport report = new StartupPreflightReport().AddPass("binary", "binary ok");
    bool enableCalled = false;

    MainWindowViewModel viewModel = CreateViewModel(
      profilesPath,
      profile,
      (_, _) => Task.FromResult(report),
      (_, _, _) =>
      {
        enableCalled = true;
        return Task.CompletedTask;
      },
      (_, _) => Task.CompletedTask);

    await viewModel.ToggleStartupCommand.ExecuteAsync(null);

    Assert.True(enableCalled);
    Assert.True(viewModel.SelectedProfile.StartAtLogin);
    Assert.True(ReadPersistedStartAtLogin(profilesPath, "profile-2"));
  }

  [Fact]
  public async Task ToggleStartupCommand_DisablesAndPersistsStartAtLoginFalse()
  {
    string profilesPath = CreateProfilesPath();
    MountProfile profile = new()
    {
      Id = "profile-3",
      StartAtLogin = true,
    };

    bool disableCalled = false;
    MainWindowViewModel viewModel = CreateViewModel(
      profilesPath,
      profile,
      (_, _) => Task.FromResult(new StartupPreflightReport().AddPass("binary", "binary ok")),
      (_, _, _) => Task.CompletedTask,
      (_, _) =>
      {
        disableCalled = true;
        return Task.CompletedTask;
      });

    await viewModel.ToggleStartupCommand.ExecuteAsync(null);

    Assert.True(disableCalled);
    Assert.False(viewModel.SelectedProfile.StartAtLogin);
    Assert.False(ReadPersistedStartAtLogin(profilesPath, "profile-3"));
  }

  [Fact]
  public async Task StartAndStopMountCommandGuards_RemainUnchangedAfterStartupToggleAttempt()
  {
    string profilesPath = CreateProfilesPath();
    MountProfile profile = new()
    {
      Id = "profile-4",
      Source = string.Empty,
    };

    MainWindowViewModel viewModel = CreateViewModel(
      profilesPath,
      profile,
      (_, _) => Task.FromResult(new StartupPreflightReport().AddCritical("mount-path", "missing mount path")),
      (_, _, _) => Task.CompletedTask,
      (_, _) => Task.CompletedTask);

    bool canStartBefore = viewModel.StartMountCommand.CanExecute(null);
    bool canStopBefore = viewModel.StopMountCommand.CanExecute(null);

    await viewModel.ToggleStartupCommand.ExecuteAsync(null);

    Assert.Equal(canStartBefore, viewModel.StartMountCommand.CanExecute(null));
    Assert.Equal(canStopBefore, viewModel.StopMountCommand.CanExecute(null));
    Assert.False(viewModel.SelectedProfile.StartAtLogin);
  }

  [Fact]
  public async Task StartMountCommand_IsBlocked_WhenSourceRemoteMissingFromRcloneConfig()
  {
    string profilesPath = CreateProfilesPath();
    MountProfile profile = new()
    {
      Id = "profile-5",
      Source = "missing-remote:/",
      MountPoint = "/tmp/test-mount",
    };

    MainWindowViewModel viewModel = CreateViewModel(
      profilesPath,
      profile,
      (_, _) => Task.FromResult(new StartupPreflightReport().AddPass("binary", "binary ok")),
      (_, _, _) => Task.CompletedTask,
      (_, _) => Task.CompletedTask,
      (_, _, _) => Task.FromResult(false));

    viewModel.AddRemoteCommand.Execute(null);
    viewModel.SelectedProfile.Name = "missing-remote";
    viewModel.SelectedProfile.Source = "missing-remote:/";
    viewModel.SelectedProfile.BackendName = "sftp";
    viewModel.SelectedProfile.BackendOptions = new Dictionary<string, string>
    {
      ["host"] = "example.invalid",
    };
    viewModel.SelectedProfile = viewModel.MountProfiles.First();

    await Task.Delay(25);

    Assert.True(viewModel.IsSourceRemoteMissingFromRcloneConfig);
    Assert.False(viewModel.StartMountCommand.CanExecute(null));
    Assert.Contains("missing-remote", viewModel.SourceRemoteConfigStatus, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task RepairMissingSourceRemoteCommand_IsAvailable_WhenLinkedRemoteExists()
  {
    string profilesPath = CreateProfilesPath();
    MountProfile profile = new()
    {
      Id = "profile-6",
      Source = "repair-remote:/",
      MountPoint = "/tmp/test-mount",
    };

    MainWindowViewModel viewModel = CreateViewModel(
      profilesPath,
      profile,
      (_, _) => Task.FromResult(new StartupPreflightReport().AddPass("binary", "binary ok")),
      (_, _, _) => Task.CompletedTask,
      (_, _) => Task.CompletedTask,
      (_, _, _) => Task.FromResult(false));

    viewModel.AddRemoteCommand.Execute(null);
    viewModel.SelectedProfile.Name = "repair-remote";
    viewModel.SelectedProfile.Source = "repair-remote:/";
    viewModel.SelectedProfile.BackendName = "sftp";
    viewModel.SelectedProfile.BackendOptions = new Dictionary<string, string>
    {
      ["host"] = "example.invalid",
    };
    viewModel.SelectedProfile = viewModel.MountProfiles.First();

    await Task.Delay(25);

    Assert.True(viewModel.CanRepairMissingSourceRemote);
    Assert.True(viewModel.RepairMissingSourceRemoteCommand.CanExecute(null));
  }

  [Fact]
  public async Task RepairMissingSourceRemoteCommand_IsAvailable_ForRcloneNfsMounts()
  {
    string profilesPath = CreateProfilesPath();
    MountProfile profile = new()
    {
      Id = "profile-8",
      Type = MountType.RcloneNfs,
      Source = "repair-nfs:/",
      MountPoint = "/tmp/test-mount",
    };

    MainWindowViewModel viewModel = CreateViewModel(
      profilesPath,
      profile,
      (_, _) => Task.FromResult(new StartupPreflightReport().AddPass("binary", "binary ok")),
      (_, _, _) => Task.CompletedTask,
      (_, _) => Task.CompletedTask,
      (_, _, _) => Task.FromResult(false));

    viewModel.AddRemoteCommand.Execute(null);
    viewModel.SelectedProfile.Name = "repair-nfs";
    viewModel.SelectedProfile.Source = "repair-nfs:/";
    viewModel.SelectedProfile.BackendName = "sftp";
    viewModel.SelectedProfile.BackendOptions = new Dictionary<string, string>
    {
      ["host"] = "example.invalid",
    };

    MountProfile nfsMount = viewModel.MountProfiles.First();
    nfsMount.Type = MountType.RcloneNfs;
    viewModel.SelectedProfile = nfsMount;

    await Task.Delay(25);

    Assert.True(viewModel.IsSourceRemoteMissingFromRcloneConfig);
    Assert.True(viewModel.CanRepairMissingSourceRemote);
    Assert.True(viewModel.RepairMissingSourceRemoteCommand.CanExecute(null));
  }

  [Fact]
  public async Task RepairMissingSourceRemoteCommand_RepairsAndUnblocksStart()
  {
    string profilesPath = CreateProfilesPath();
    MountProfile profile = new()
    {
      Id = "profile-7",
      Source = "recover-remote:/",
      MountPoint = "/tmp/test-mount",
    };

    List<string> repairedAliases = new();
    MainWindowViewModel viewModel = CreateViewModel(
      profilesPath,
      profile,
      (_, _) => Task.FromResult(new StartupPreflightReport().AddPass("binary", "binary ok")),
      (_, _, _) => Task.CompletedTask,
      (_, _) => Task.CompletedTask,
      (_, remoteAlias, _) => Task.FromResult(repairedAliases.Contains(remoteAlias)),
      (mount, _) =>
      {
        repairedAliases.Add(mount.Source.Split(':')[0]);
        return Task.CompletedTask;
      });

    viewModel.AddRemoteCommand.Execute(null);
    viewModel.SelectedProfile.Name = "recover-remote";
    viewModel.SelectedProfile.Source = "recover-remote:/";
    viewModel.SelectedProfile.BackendName = "sftp";
    viewModel.SelectedProfile.BackendOptions = new Dictionary<string, string>
    {
      ["host"] = "example.invalid",
    };
    viewModel.SelectedProfile = viewModel.MountProfiles.First();

    await Task.Delay(25);

    await viewModel.RepairMissingSourceRemoteCommand.ExecuteAsync(null);

    Assert.Contains("recover-remote", repairedAliases);
    Assert.False(viewModel.IsSourceRemoteMissingFromRcloneConfig);
    Assert.True(viewModel.StartMountCommand.CanExecute(null));
  }

  [Fact]
  public void SelectedWindowCloseBehavior_PersistsToProfilesFile()
  {
    string profilesPath = CreateProfilesPath();
    MountProfile profile = new() { Id = "profile-9" };

    MainWindowViewModel viewModel = CreateViewModel(
      profilesPath,
      profile,
      (_, _) => Task.FromResult(new StartupPreflightReport().AddPass("binary", "binary ok")),
      (_, _, _) => Task.CompletedTask,
      (_, _) => Task.CompletedTask);

    viewModel.SelectedWindowCloseBehavior = WindowCloseBehavior.MinimizeToDock;

    string json = File.ReadAllText(profilesPath);
    using JsonDocument document = JsonDocument.Parse(json);
    int closeBehavior = document.RootElement[0].GetProperty("WindowCloseBehavior").GetInt32();

    Assert.Equal((int)WindowCloseBehavior.MinimizeToDock, closeBehavior);
  }

  private MainWindowViewModel CreateViewModel(
    string profilesPath,
    MountProfile profile,
    Func<MountProfile, CancellationToken, Task<StartupPreflightReport>> startupPreflightRunner,
    Func<MountProfile, string, CancellationToken, Task> startupEnableRunner,
    Func<MountProfile, CancellationToken, Task> startupDisableRunner,
    Func<MountProfile, string, CancellationToken, Task<bool>>? sourceRemoteExistsInRcloneConfigRunner = null,
    Func<MountProfile, CancellationToken, Task>? repairMissingSourceRemoteRunner = null)
  {
    MainWindowViewModel viewModel = new(
      profilesPath,
      startupPreflightRunner: startupPreflightRunner,
      startupEnableRunner: startupEnableRunner,
      startupDisableRunner: startupDisableRunner,
      sourceRemoteExistsInRcloneConfigRunner: sourceRemoteExistsInRcloneConfigRunner,
      repairMissingSourceRemoteRunner: repairMissingSourceRemoteRunner,
      startupEnabledProbe: _ => false,
      loadStartupData: false);

    viewModel.SelectedProfile.Id = profile.Id;
    viewModel.SelectedProfile.Source = profile.Source;
    viewModel.SelectedProfile.MountPoint = profile.MountPoint;
    viewModel.SelectedProfile.StartAtLogin = profile.StartAtLogin;

    return viewModel;
  }

  private string CreateProfilesPath()
  {
    Directory.CreateDirectory(_tempRoot);
    return Path.Combine(_tempRoot, "profiles.json");
  }

  private static bool ReadPersistedStartAtLogin(string profilesPath, string profileId)
  {
    string json = File.ReadAllText(profilesPath);
    using JsonDocument document = JsonDocument.Parse(json);
    foreach (JsonElement entry in document.RootElement.EnumerateArray())
    {
      string? id = entry.GetProperty("Id").GetString();
      if (!string.Equals(id, profileId, StringComparison.Ordinal))
      {
        continue;
      }

      return entry.GetProperty("StartAtLogin").GetBoolean();
    }

    throw new InvalidOperationException($"Persisted profile '{profileId}' was not found.");
  }
}
