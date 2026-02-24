using RcloneMountManager.Core.Models;
using RcloneMountManager.ViewModels;
using System.Text.Json;

namespace RcloneMountManager.Tests.ViewModels;

public sealed class MainWindowViewModelStartupTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"main-window-startup-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ToggleStartupCommand_BlocksEnableWhenCriticalPreflightFails()
    {
        var profilesPath = CreateProfilesPath();
        var profile = new MountProfile { Id = "profile-1" };
        var report = new StartupPreflightReport().AddCritical("binary", "binary missing");
        var enableCalled = false;

        var viewModel = CreateViewModel(
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
        var profilesPath = CreateProfilesPath();
        var profile = new MountProfile { Id = "profile-2" };
        var report = new StartupPreflightReport().AddPass("binary", "binary ok");
        var enableCalled = false;

        var viewModel = CreateViewModel(
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
        var profilesPath = CreateProfilesPath();
        var profile = new MountProfile
        {
            Id = "profile-3",
            StartAtLogin = true,
        };

        var disableCalled = false;
        var viewModel = CreateViewModel(
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
        var profilesPath = CreateProfilesPath();
        var profile = new MountProfile
        {
            Id = "profile-4",
            Source = string.Empty,
        };

        var viewModel = CreateViewModel(
            profilesPath,
            profile,
            (_, _) => Task.FromResult(new StartupPreflightReport().AddCritical("mount-path", "missing mount path")),
            (_, _, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask);

        var canStartBefore = viewModel.StartMountCommand.CanExecute(null);
        var canStopBefore = viewModel.StopMountCommand.CanExecute(null);

        await viewModel.ToggleStartupCommand.ExecuteAsync(null);

        Assert.Equal(canStartBefore, viewModel.StartMountCommand.CanExecute(null));
        Assert.Equal(canStopBefore, viewModel.StopMountCommand.CanExecute(null));
        Assert.False(viewModel.SelectedProfile.StartAtLogin);
    }

    private MainWindowViewModel CreateViewModel(
        string profilesPath,
        MountProfile profile,
        Func<MountProfile, CancellationToken, Task<StartupPreflightReport>> startupPreflightRunner,
        Func<MountProfile, string, CancellationToken, Task> startupEnableRunner,
        Func<MountProfile, CancellationToken, Task> startupDisableRunner)
    {
        var viewModel = new MainWindowViewModel(
            profilesFilePath: profilesPath,
            startupPreflightRunner: startupPreflightRunner,
            startupEnableRunner: startupEnableRunner,
            startupDisableRunner: startupDisableRunner,
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
        var json = File.ReadAllText(profilesPath);
        using var document = JsonDocument.Parse(json);
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            var id = entry.GetProperty("Id").GetString();
            if (!string.Equals(id, profileId, StringComparison.Ordinal))
            {
                continue;
            }

            return entry.GetProperty("StartAtLogin").GetBoolean();
        }

        throw new InvalidOperationException($"Persisted profile '{profileId}' was not found.");
    }
}
