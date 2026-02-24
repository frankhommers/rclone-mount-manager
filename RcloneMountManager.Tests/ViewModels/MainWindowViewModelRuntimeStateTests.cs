using RcloneMountManager.Core.Models;
using RcloneMountManager.ViewModels;
using System.Collections.Concurrent;

namespace RcloneMountManager.Tests.ViewModels;

public sealed class MainWindowViewModelRuntimeStateTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"main-window-runtime-state-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task StartMountCommand_SetsMountingBeforeCompletingWithMountedState()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mountedState = CreateState(MountLifecycleState.Mounted, MountHealthState.Healthy);

        var viewModel = CreateViewModel(
            mountStartRunner: async (_, _, _) => await gate.Task,
            runtimeStateVerifier: (_, _) => Task.FromResult(mountedState));

        var startTask = viewModel.StartMountCommand.ExecuteAsync(null);

        await WaitUntilAsync(() => viewModel.SelectedProfile.RuntimeState.Lifecycle is MountLifecycleState.Mounting);
        Assert.Equal(MountLifecycleState.Mounting, viewModel.SelectedProfile.RuntimeState.Lifecycle);

        gate.SetResult();
        await startTask;

        Assert.Equal(MountLifecycleState.Mounted, viewModel.SelectedProfile.RuntimeState.Lifecycle);
        Assert.Equal(MountHealthState.Healthy, viewModel.SelectedProfile.RuntimeState.Health);
        Assert.Contains("Lifecycle: mounted", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartMountCommand_MapsVerificationFailureToFailedState()
    {
        var failedState = CreateState(MountLifecycleState.Failed, MountHealthState.Failed, "mount missing");
        var viewModel = CreateViewModel(runtimeStateVerifier: (_, _) => Task.FromResult(failedState));

        await viewModel.StartMountCommand.ExecuteAsync(null);

        Assert.Equal(MountLifecycleState.Failed, viewModel.SelectedProfile.RuntimeState.Lifecycle);
        Assert.Equal(MountHealthState.Failed, viewModel.SelectedProfile.RuntimeState.Health);
        Assert.Contains("Health: failed", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopMountCommand_SetsIdleWhenProfileIsNoLongerMounted()
    {
        var viewModel = CreateViewModel(mountedProbe: (_, _) => Task.FromResult(false));
        viewModel.SelectedProfile.RuntimeState = CreateState(MountLifecycleState.Mounted, MountHealthState.Healthy);

        await viewModel.StopMountCommand.ExecuteAsync(null);

        Assert.Equal(MountLifecycleState.Idle, viewModel.SelectedProfile.RuntimeState.Lifecycle);
        Assert.Equal(MountHealthState.Unknown, viewModel.SelectedProfile.RuntimeState.Health);
        Assert.Contains("Lifecycle: idle", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MountHealthState.Degraded)]
    [InlineData(MountHealthState.Failed)]
    public async Task RefreshStatusCommand_MapsHealthVerdictFromRuntimeState(MountHealthState health)
    {
        var lifecycle = health is MountHealthState.Failed ? MountLifecycleState.Failed : MountLifecycleState.Mounted;
        var runtimeState = CreateState(lifecycle, health, "probe result");
        var viewModel = CreateViewModel(runtimeStateVerifier: (_, _) => Task.FromResult(runtimeState));

        await viewModel.RefreshStatusCommand.ExecuteAsync(null);

        Assert.Equal(health, viewModel.SelectedProfile.RuntimeState.Health);
        Assert.Contains($"Health: {health.ToString().ToLowerInvariant()}", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeRuntimeMonitoring_ProbesRcEnabledProfilesForOrphanAdoption()
    {
        var viewModel = CreateViewModel(runtimeRefreshWaiter: (_, _) => Task.FromResult(false));

        var profile = viewModel.SelectedProfile;
        profile.StartAtLogin = false;
        profile.EnableRemoteControl = true;
        profile.RcPort = 1;

        viewModel.InitializeRuntimeMonitoring();

        await WaitUntilAsync(() => viewModel.Logs.Any(entry => entry.Contains("Probing 1 mount profiles for running orphans...", StringComparison.Ordinal)));

        viewModel.StopRuntimeMonitoring();
    }

    [Fact]
    public async Task InitializeRuntimeMonitoring_VerifiesOnlyStartAtLoginProfiles()
    {
        var verifiedProfileIds = new ConcurrentBag<string>();
        var viewModel = CreateViewModel(
            runtimeStateVerifier: (profile, _) =>
            {
                verifiedProfileIds.Add(profile.Id);
                return Task.FromResult(CreateState(MountLifecycleState.Mounted, MountHealthState.Healthy));
            },
            runtimeRefreshWaiter: (_, _) => Task.FromResult(false));

        var startupProfile = viewModel.SelectedProfile;
        startupProfile.StartAtLogin = true;

        viewModel.AddProfileCommand.Execute(null);
        var nonStartupProfile = viewModel.SelectedProfile;
        nonStartupProfile.StartAtLogin = false;

        viewModel.InitializeRuntimeMonitoring();

        await WaitUntilAsync(() => startupProfile.RuntimeState.Health is MountHealthState.Healthy);

        Assert.Contains(startupProfile.Id, verifiedProfileIds);
        Assert.DoesNotContain(nonStartupProfile.Id, verifiedProfileIds);
        viewModel.StopRuntimeMonitoring();
    }

    [Fact]
    public async Task InitializeRuntimeMonitoring_MapsDegradedAndFailedStartupStates()
    {
        var startupStates = new Dictionary<string, ProfileRuntimeState>();
        var viewModel = CreateViewModel(
            runtimeStateVerifier: (profile, _) => Task.FromResult(startupStates[profile.Id]),
            runtimeRefreshWaiter: (_, _) => Task.FromResult(false));

        var degradedProfile = viewModel.SelectedProfile;
        degradedProfile.StartAtLogin = true;
        viewModel.AddProfileCommand.Execute(null);
        var failedProfile = viewModel.SelectedProfile;
        failedProfile.StartAtLogin = true;
        viewModel.AddProfileCommand.Execute(null);
        var skippedProfile = viewModel.SelectedProfile;
        skippedProfile.StartAtLogin = false;

        startupStates[degradedProfile.Id] = CreateState(MountLifecycleState.Mounted, MountHealthState.Degraded, "probe timeout");
        startupStates[failedProfile.Id] = CreateState(MountLifecycleState.Failed, MountHealthState.Failed, "mount missing");
        startupStates[skippedProfile.Id] = CreateState(MountLifecycleState.Mounted, MountHealthState.Healthy);

        viewModel.InitializeRuntimeMonitoring();

        await WaitUntilAsync(() =>
            degradedProfile.RuntimeState.Health is MountHealthState.Degraded &&
            failedProfile.RuntimeState.Health is MountHealthState.Failed);

        Assert.Equal(MountHealthState.Degraded, degradedProfile.RuntimeState.Health);
        Assert.Equal(MountLifecycleState.Mounted, degradedProfile.RuntimeState.Lifecycle);
        Assert.Equal(MountHealthState.Failed, failedProfile.RuntimeState.Health);
        Assert.Equal(MountLifecycleState.Failed, failedProfile.RuntimeState.Lifecycle);
        Assert.Equal(MountHealthState.Unknown, skippedProfile.RuntimeState.Health);
        viewModel.StopRuntimeMonitoring();
    }

    [Fact]
    public async Task InitializeRuntimeMonitoring_PeriodicRefreshUpdatesAllProfiles()
    {
        var ticks = new Queue<bool>(new[] { true, false });
        var firstTickAt = DateTimeOffset.UtcNow;
        var secondTickAt = firstTickAt.AddSeconds(10);

        var profileStates = new Dictionary<string, Queue<ProfileRuntimeState>>();
        var viewModel = CreateViewModel(
            runtimeStateVerifier: (profile, _) => Task.FromResult(profileStates[profile.Id].Dequeue()),
            runtimeRefreshWaiter: (_, _) => Task.FromResult(ticks.Count > 0 && ticks.Dequeue()));

        var firstProfile = viewModel.SelectedProfile;
        firstProfile.StartAtLogin = false;
        viewModel.AddProfileCommand.Execute(null);
        var secondProfile = viewModel.SelectedProfile;
        secondProfile.StartAtLogin = false;

        profileStates[firstProfile.Id] = new Queue<ProfileRuntimeState>(new[]
        {
            new ProfileRuntimeState(MountLifecycleState.Mounted, MountHealthState.Healthy, firstTickAt, null),
        });
        profileStates[secondProfile.Id] = new Queue<ProfileRuntimeState>(new[]
        {
            new ProfileRuntimeState(MountLifecycleState.Mounted, MountHealthState.Degraded, secondTickAt, "probe lag"),
        });

        viewModel.InitializeRuntimeMonitoring();

        await WaitUntilAsync(() =>
            firstProfile.RuntimeState.LastCheckedAt >= firstTickAt &&
            secondProfile.RuntimeState.LastCheckedAt >= secondTickAt);

        Assert.Equal(MountHealthState.Healthy, firstProfile.RuntimeState.Health);
        Assert.Equal(MountHealthState.Degraded, secondProfile.RuntimeState.Health);
        viewModel.StopRuntimeMonitoring();
    }

    private MainWindowViewModel CreateViewModel(
        Func<MountProfile, Action<string>, CancellationToken, Task>? mountStartRunner = null,
        Func<MountProfile, Action<string>, CancellationToken, Task>? mountStopRunner = null,
        Func<MountProfile, CancellationToken, Task<bool>>? mountedProbe = null,
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
            mountStopRunner: mountStopRunner,
            mountedProbe: mountedProbe,
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

        throw new TimeoutException("Timed out waiting for expected state transition.");
    }
}
