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
        Assert.Contains(viewModel.Logs, entry => entry.Contains(firstProfile.Id, StringComparison.Ordinal));
        Assert.DoesNotContain(viewModel.Logs, entry => entry.Contains(secondProfile.Id, StringComparison.Ordinal));

        viewModel.SelectedDiagnosticsProfileId = secondProfile.Id;
        Assert.Contains(viewModel.Logs, entry => entry.Contains(secondProfile.Id, StringComparison.Ordinal));
        Assert.DoesNotContain(viewModel.Logs, entry => entry.Contains(firstProfile.Id, StringComparison.Ordinal));
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
        Assert.Contains(viewModel.Logs, entry => entry.Contains("Startup monitor initialization started.", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(viewModel.Logs, entry => entry.Contains("Startup verification:", StringComparison.OrdinalIgnoreCase));
        Assert.All(viewModel.Logs, entry => Assert.True(entry.Contains("[startup/", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(viewModel.Logs, entry => entry.Contains("[manualstart/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(viewModel.Logs, entry => entry.Contains("[runtimerefresh/", StringComparison.OrdinalIgnoreCase));

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
        Assert.All(viewModel.Logs, entry => Assert.True(entry.Contains("[startup/", StringComparison.OrdinalIgnoreCase)));

        viewModel.SelectedDiagnosticsProfileId = secondProfile.Id;
        Assert.All(viewModel.Logs, entry => Assert.True(entry.Contains(secondProfile.Id, StringComparison.Ordinal)));

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
}
