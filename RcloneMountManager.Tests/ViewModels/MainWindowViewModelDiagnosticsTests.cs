using RcloneMountManager.Core.Models;
using RcloneMountManager.ViewModels;

namespace RcloneMountManager.Tests.ViewModels;

public sealed class MainWindowViewModelDiagnosticsTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"main-window-diagnostics-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ShowDiagnosticsView_DefaultsFalse()
    {
        var viewModel = CreateViewModel();
        Assert.False(viewModel.ShowDiagnosticsView);
    }

    [Fact]
    public void SelectDiagnostics_ShowsDiagnosticsView()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectDiagnosticsCommand.Execute(null);

        Assert.True(viewModel.ShowDiagnosticsView);
        Assert.Equal("Diagnostics", viewModel.WorkspaceTitle);
        Assert.True(viewModel.ShowDiagnosticsView);
        Assert.False(viewModel.ShowRemoteEditorContent);
        Assert.False(viewModel.ShowMountEditorContent);
    }

    [Fact]
    public void SelectProfileAfterDiagnostics_HidesDiagnosticsView()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectDiagnosticsCommand.Execute(null);
        Assert.True(viewModel.ShowDiagnosticsView);

        viewModel.AddProfileCommand.Execute(null);
        Assert.False(viewModel.ShowDiagnosticsView);
    }

    [Fact]
    public async Task DiagnosticsCategoryFilter_FiltersRemotesAndMounts()
    {
        var viewModel = CreateViewModel(
            mountStartRunner: (profile, log, _) =>
            {
                log($"runner callback for {profile.Id}");
                return Task.CompletedTask;
            },
            runtimeStateVerifier: (_, _) => Task.FromResult(CreateState(MountLifecycleState.Mounted, MountHealthState.Healthy)));

        viewModel.AddRemoteCommand.Execute(null);
        var remoteProfile = viewModel.SelectedProfile;

        viewModel.AddProfileCommand.Execute(null);
        var mountProfile = viewModel.SelectedProfile;
        await viewModel.StartMountCommand.ExecuteAsync(null);

        viewModel.SelectDiagnosticsCommand.Execute(null);

        viewModel.SelectedDiagnosticsCategoryFilter = "Mounts";
        Assert.All(viewModel.DiagnosticsRows, row =>
        {
            var isMount = viewModel.MountProfiles.Any(p => string.Equals(p.Id, row.ProfileId, StringComparison.OrdinalIgnoreCase));
            Assert.True(isMount);
        });
    }

    [Fact]
    public async Task DiagnosticsSearchText_FiltersMessages()
    {
        var viewModel = CreateViewModel(
            mountStartRunner: (profile, log, _) =>
            {
                log("unique-search-token");
                log("other message");
                return Task.CompletedTask;
            },
            runtimeStateVerifier: (_, _) => Task.FromResult(CreateState(MountLifecycleState.Mounted, MountHealthState.Healthy)));

        await viewModel.StartMountCommand.ExecuteAsync(null);
        viewModel.SelectDiagnosticsCommand.Execute(null);

        viewModel.DiagnosticsSearchText = "unique-search-token";
        Assert.NotEmpty(viewModel.DiagnosticsRows);
        Assert.All(viewModel.DiagnosticsRows, row =>
            Assert.Contains("unique-search-token", row.MessageText, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DiagnosticsProfileFilter_ShowsOnlyMatchingProfileEvents()
    {
        var viewModel = CreateViewModel(
            mountStartRunner: (profile, log, _) =>
            {
                log($"runner callback for {profile.Id}");
                return Task.CompletedTask;
            },
            runtimeStateVerifier: (_, _) => Task.FromResult(CreateState(MountLifecycleState.Mounted, MountHealthState.Healthy)));

        var firstProfile = viewModel.SelectedProfile;
        viewModel.AddProfileCommand.Execute(null);
        var secondProfile = viewModel.SelectedProfile;

        viewModel.SelectedProfile = firstProfile;
        await viewModel.StartMountCommand.ExecuteAsync(null);

        viewModel.SelectedProfile = secondProfile;
        await viewModel.StartMountCommand.ExecuteAsync(null);

        viewModel.SelectedDiagnosticsProfileId = firstProfile.Id;
        Assert.NotEmpty(viewModel.DiagnosticsRows);
        Assert.All(viewModel.DiagnosticsRows, row => Assert.Equal(firstProfile.Id, row.ProfileId));
        Assert.All(viewModel.DiagnosticsRows, AssertTimestampPresent);

        viewModel.SelectedProfile = secondProfile;
        Assert.All(viewModel.DiagnosticsRows, row => Assert.Equal(firstProfile.Id, row.ProfileId));

        viewModel.SelectedDiagnosticsProfileId = secondProfile.Id;
        Assert.NotEmpty(viewModel.DiagnosticsRows);
        Assert.All(viewModel.DiagnosticsRows, row => Assert.Equal(secondProfile.Id, row.ProfileId));
        Assert.All(viewModel.DiagnosticsRows, AssertTimestampPresent);
    }

    [Fact]
    public async Task StartupTimelineOnly_IncludesStartupEventsAndExcludesManualAndRefreshEvents()
    {
        var viewModel = CreateViewModel(
            runtimeStateVerifier: (_, _) => Task.FromResult(CreateState(MountLifecycleState.Mounted, MountHealthState.Healthy)),
            runtimeRefreshWaiter: (_, _) => Task.FromResult(false));

        var startupProfile = viewModel.SelectedProfile;
        startupProfile.StartAtLogin = true;
        viewModel.InitializeRuntimeMonitoring();

        await WaitUntilAsync(() => viewModel.Logs.Any(entry => entry.Contains("Startup verification:", StringComparison.OrdinalIgnoreCase)));
        await viewModel.StartMountCommand.ExecuteAsync(null);

        viewModel.StartupTimelineOnly = true;

        Assert.NotEmpty(viewModel.Logs);
        Assert.Contains(viewModel.DiagnosticsRows, row => row.MessageText.Contains("Startup monitor initialization started.", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(viewModel.DiagnosticsRows, row => row.MessageText.Contains("Startup verification:", StringComparison.OrdinalIgnoreCase));
        Assert.All(viewModel.DiagnosticsRows, row => Assert.StartsWith("startup/", row.StageText, StringComparison.OrdinalIgnoreCase));
        Assert.All(viewModel.DiagnosticsRows, AssertTimestampPresent);
        Assert.DoesNotContain(viewModel.DiagnosticsRows, row => row.StageText.StartsWith("manualstart/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(viewModel.DiagnosticsRows, row => row.StageText.StartsWith("runtimerefresh/", StringComparison.OrdinalIgnoreCase));

        viewModel.StopRuntimeMonitoring();
    }

    [Fact]
    public async Task DiagnosticsTimeline_ProjectsEventsInChronologicalOrder()
    {
        var viewModel = CreateViewModel(
            mountStartRunner: (profile, log, _) =>
            {
                log($"runner callback for {profile.Id}");
                return Task.CompletedTask;
            },
            runtimeStateVerifier: (_, _) => Task.FromResult(CreateState(MountLifecycleState.Mounted, MountHealthState.Healthy)));

        var profile = viewModel.SelectedProfile;
        viewModel.SelectedDiagnosticsProfileId = profile.Id;
        await viewModel.StartMountCommand.ExecuteAsync(null);

        Assert.NotEmpty(viewModel.DiagnosticsRows);
        Assert.All(viewModel.DiagnosticsRows, AssertTimestampPresent);
        Assert.All(viewModel.DiagnosticsRows, row => Assert.False(string.IsNullOrWhiteSpace(row.SeverityText)));
        Assert.All(viewModel.DiagnosticsRows, row => Assert.False(string.IsNullOrWhiteSpace(row.StageText)));
        Assert.All(viewModel.DiagnosticsRows, row => Assert.False(string.IsNullOrWhiteSpace(row.MessageText)));

        var startedIndex = FindLogIndex(viewModel.Logs, "Starting mount");
        var callbackIndex = FindLogIndex(viewModel.Logs, "runner callback");
        var statusIndex = FindLogIndex(viewModel.Logs, "Status for");

        Assert.True(startedIndex < callbackIndex, "Initialization event should appear before execution event.");
        Assert.True(callbackIndex < statusIndex, "Execution event should appear before completion event.");
    }

    [Fact]
    public async Task ChangingDiagnosticsFilters_RecomputesTimelineDeterministically()
    {
        var viewModel = CreateViewModel(
            mountStartRunner: (profile, log, _) =>
            {
                log($"runner callback for {profile.Id}");
                return Task.CompletedTask;
            },
            runtimeStateVerifier: (_, _) => Task.FromResult(CreateState(MountLifecycleState.Mounted, MountHealthState.Healthy)),
            runtimeRefreshWaiter: (_, _) => Task.FromResult(false));

        var firstProfile = viewModel.SelectedProfile;
        firstProfile.StartAtLogin = true;

        viewModel.AddProfileCommand.Execute(null);
        var secondProfile = viewModel.SelectedProfile;
        secondProfile.StartAtLogin = true;

        viewModel.InitializeRuntimeMonitoring();
        await WaitUntilAsync(() =>
            firstProfile.RuntimeState.Health is MountHealthState.Healthy &&
            secondProfile.RuntimeState.Health is MountHealthState.Healthy);

        viewModel.SelectedProfile = firstProfile;
        await viewModel.StartMountCommand.ExecuteAsync(null);

        viewModel.SelectedProfile = secondProfile;
        await viewModel.StartMountCommand.ExecuteAsync(null);

        viewModel.SelectedDiagnosticsProfileId = firstProfile.Id;
        viewModel.StartupTimelineOnly = false;
        var firstProfileAllEvents = viewModel.Logs.ToList();

        viewModel.StartupTimelineOnly = true;
        Assert.All(viewModel.DiagnosticsRows, row => Assert.StartsWith("startup/", row.StageText, StringComparison.OrdinalIgnoreCase));
        Assert.All(viewModel.DiagnosticsRows, AssertTimestampPresent);

        viewModel.SelectedDiagnosticsProfileId = secondProfile.Id;
        Assert.All(viewModel.DiagnosticsRows, row => Assert.Equal(secondProfile.Id, row.ProfileId));

        viewModel.SelectedDiagnosticsProfileId = firstProfile.Id;
        viewModel.StartupTimelineOnly = false;
        Assert.Equal(firstProfileAllEvents, viewModel.Logs);

        viewModel.StopRuntimeMonitoring();
    }

    private MainWindowViewModel CreateViewModel(
        Func<MountProfile, Action<string>, CancellationToken, Task>? mountStartRunner = null,
        Func<MountProfile, CancellationToken, Task<ProfileRuntimeState>>? runtimeStateVerifier = null,
        Func<TimeSpan, CancellationToken, Task<bool>>? runtimeRefreshWaiter = null)
    {
        async Task<IReadOnlyList<ProfileRuntimeState>> RuntimeStateBatchVerifier(
            IEnumerable<MountProfile> profiles,
            CancellationToken cancellationToken)
        {
            var states = new List<ProfileRuntimeState>();
            foreach (var profile in profiles)
            {
                states.Add(await (runtimeStateVerifier?.Invoke(profile, cancellationToken)
                    ?? Task.FromResult(CreateState(MountLifecycleState.Idle, MountHealthState.Unknown))));
            }

            return states;
        }

        return new MainWindowViewModel(
            profilesFilePath: CreateProfilesPath(),
            mountStartRunner: mountStartRunner,
            runtimeStateVerifier: runtimeStateVerifier,
            startupEnabledProbe: _ => false,
            runtimeRefreshWaiter: runtimeRefreshWaiter,
            runtimeStateBatchVerifier: RuntimeStateBatchVerifier,
            loadStartupData: false);
    }

    private string CreateProfilesPath()
    {
        Directory.CreateDirectory(_tempRoot);
        return Path.Combine(_tempRoot, "profiles.json");
    }

    private static ProfileRuntimeState CreateState(MountLifecycleState lifecycle, MountHealthState health, string? errorText = null)
        => new(lifecycle, health, DateTimeOffset.UtcNow, errorText);

    private static int FindLogIndex(IEnumerable<string> logs, string fragment)
    {
        var index = 0;
        foreach (var log in logs)
        {
            if (log.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }

            index++;
        }

        throw new Xunit.Sdk.XunitException($"Expected to find log containing '{fragment}'.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Timed out waiting for diagnostics expectation.");
    }

    private static void AssertTimestampPresent(MainWindowViewModel.DiagnosticsTimelineRow row)
    {
        Assert.True(
            DateTime.TryParseExact(
                row.TimestampText,
                "yyyy-MM-dd HH:mm:ss",
                provider: null,
                System.Globalization.DateTimeStyles.None,
                out _),
            $"Expected timestamp in yyyy-MM-dd HH:mm:ss format but got '{row.TimestampText}'.");
    }
}
