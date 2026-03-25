using RcloneMountManager.Core.Models;
using System.Collections.Concurrent;
using RcloneMountManager.GUI.ViewModels;

namespace RcloneMountManager.Tests.ViewModels;

public sealed class MainWindowViewModelRuntimeStateTests : IDisposable
{
  private readonly string _tempRoot = Path.Combine(
    Path.GetTempPath(),
    $"main-window-runtime-state-tests-{Guid.NewGuid():N}");

  private readonly List<MainWindowViewModel> _viewModels = [];

  public void Dispose()
  {
    foreach (MainWindowViewModel viewModel in _viewModels)
    {
      viewModel.Dispose();
    }

    if (Directory.Exists(_tempRoot))
    {
      Directory.Delete(_tempRoot, true);
    }
  }

  [Fact]
  public async Task StartMountCommand_SetsMountingBeforeCompletingWithMountedState()
  {
    TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    ProfileRuntimeState mountedState = CreateState(MountLifecycleState.Mounted, MountHealthState.Healthy);

    MainWindowViewModel viewModel = CreateViewModel(
      async (_, _) => await gate.Task,
      runtimeStateVerifier: (_, _) => Task.FromResult(mountedState));

    Task startTask = viewModel.StartMountCommand.ExecuteAsync(null);

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
    ProfileRuntimeState failedState = CreateState(MountLifecycleState.Failed, MountHealthState.Failed, "mount missing");
    MainWindowViewModel viewModel = CreateViewModel(
      mountStartRunner: (_, _) => Task.CompletedTask,
      runtimeStateVerifier: (_, _) => Task.FromResult(failedState));

    await viewModel.StartMountCommand.ExecuteAsync(null);

    Assert.Equal(MountLifecycleState.Failed, viewModel.SelectedProfile.RuntimeState.Lifecycle);
    Assert.Equal(MountHealthState.Failed, viewModel.SelectedProfile.RuntimeState.Health);
    Assert.Contains("Health: failed", viewModel.StatusText, StringComparison.Ordinal);
  }

  [Fact]
  public async Task StopMountCommand_SetsIdleWhenProfileIsNoLongerMounted()
  {
    MainWindowViewModel viewModel = CreateViewModel(mountedProbe: (_, _) => Task.FromResult(false));
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
    MountLifecycleState lifecycle =
      health is MountHealthState.Failed ? MountLifecycleState.Failed : MountLifecycleState.Mounted;
    ProfileRuntimeState runtimeState = CreateState(lifecycle, health, "probe result");
    MainWindowViewModel viewModel = CreateViewModel(runtimeStateVerifier: (_, _) => Task.FromResult(runtimeState));

    await viewModel.RefreshStatusCommand.ExecuteAsync(null);

    Assert.Equal(health, viewModel.SelectedProfile.RuntimeState.Health);
    Assert.Contains($"Health: {health.ToString().ToLowerInvariant()}", viewModel.StatusText, StringComparison.Ordinal);
  }

  [Fact]
  public async Task InitializeRuntimeMonitoring_ProbesRcEnabledProfilesForOrphanAdoption()
  {
    MainWindowViewModel viewModel = CreateViewModel(runtimeRefreshWaiter: (_, _) => Task.FromResult(false));

    MountProfile profile = viewModel.SelectedProfile;
    profile.StartAtLogin = false;
    profile.EnableRemoteControl = true;
    profile.RcPort = 1;

    viewModel.InitializeRuntimeMonitoring();

    await WaitUntilAsync(() => viewModel.Logs.Any(entry => entry.Contains(
                                                    "Probing 1 mount profiles for running orphans...",
                                                    StringComparison.Ordinal)));

    viewModel.StopRuntimeMonitoring();
  }

  [Fact]
  public async Task InitializeRuntimeMonitoring_VerifiesAllMountProfiles()
  {
    ConcurrentBag<string> verifiedProfileIds = new();
    MainWindowViewModel viewModel = CreateViewModel(
      runtimeStateVerifier: (profile, _) =>
      {
        verifiedProfileIds.Add(profile.Id);
        return Task.FromResult(CreateState(MountLifecycleState.Mounted, MountHealthState.Healthy));
      },
      runtimeRefreshWaiter: (_, _) => Task.FromResult(false));

    MountProfile startupProfile = viewModel.SelectedProfile;
    startupProfile.StartAtLogin = true;

    viewModel.AddProfileCommand.Execute(null);
    MountProfile nonStartupProfile = viewModel.SelectedProfile;
    nonStartupProfile.StartAtLogin = false;

    viewModel.InitializeRuntimeMonitoring();

    await WaitUntilAsync(() =>
                           startupProfile.RuntimeState.Health is MountHealthState.Healthy &&
                           nonStartupProfile.RuntimeState.Health is MountHealthState.Healthy);

    Assert.Contains(startupProfile.Id, verifiedProfileIds);
    Assert.Contains(nonStartupProfile.Id, verifiedProfileIds);
    viewModel.StopRuntimeMonitoring();
  }

  [Fact]
  public async Task InitializeRuntimeMonitoring_MapsDegradedAndFailedStartupStates()
  {
    Dictionary<string, ProfileRuntimeState> startupStates = new();
    MainWindowViewModel viewModel = CreateViewModel(
      runtimeStateVerifier: (profile, _) => Task.FromResult(startupStates[profile.Id]),
      runtimeRefreshWaiter: (_, _) => Task.FromResult(false));

    MountProfile degradedProfile = viewModel.SelectedProfile;
    degradedProfile.StartAtLogin = true;
    viewModel.AddProfileCommand.Execute(null);
    MountProfile failedProfile = viewModel.SelectedProfile;
    failedProfile.StartAtLogin = true;
    viewModel.AddProfileCommand.Execute(null);
    MountProfile idleProfile = viewModel.SelectedProfile;
    idleProfile.StartAtLogin = false;

    startupStates[degradedProfile.Id] = CreateState(
      MountLifecycleState.Mounted,
      MountHealthState.Degraded,
      "probe timeout");
    startupStates[failedProfile.Id] = CreateState(MountLifecycleState.Failed, MountHealthState.Failed, "mount missing");
    startupStates[idleProfile.Id] = CreateState(MountLifecycleState.Idle, MountHealthState.Unknown);

    viewModel.InitializeRuntimeMonitoring();

    await WaitUntilAsync(() =>
                           degradedProfile.RuntimeState.Health is MountHealthState.Degraded &&
                           failedProfile.RuntimeState.Health is MountHealthState.Failed);

    Assert.Equal(MountHealthState.Degraded, degradedProfile.RuntimeState.Health);
    Assert.Equal(MountLifecycleState.Mounted, degradedProfile.RuntimeState.Lifecycle);
    Assert.Equal(MountHealthState.Failed, failedProfile.RuntimeState.Health);
    Assert.Equal(MountLifecycleState.Failed, failedProfile.RuntimeState.Lifecycle);
    Assert.Equal(MountHealthState.Unknown, idleProfile.RuntimeState.Health);
    viewModel.StopRuntimeMonitoring();
  }

  [Fact]
  public async Task InitializeRuntimeMonitoring_PeriodicRefreshUpdatesAllProfiles()
  {
    Queue<bool> ticks = new(new[] {true, false});
    DateTimeOffset firstTickAt = DateTimeOffset.UtcNow;
    DateTimeOffset secondTickAt = firstTickAt.AddSeconds(10);

    Dictionary<string, Queue<ProfileRuntimeState>> profileStates = new();
    MainWindowViewModel viewModel = CreateViewModel(
      runtimeStateVerifier: (profile, _) => Task.FromResult(profileStates[profile.Id].Dequeue()),
      runtimeRefreshWaiter: (_, _) => Task.FromResult(ticks.Count > 0 && ticks.Dequeue()));

    MountProfile firstProfile = viewModel.SelectedProfile;
    firstProfile.StartAtLogin = false;
    viewModel.AddProfileCommand.Execute(null);
    MountProfile secondProfile = viewModel.SelectedProfile;
    secondProfile.StartAtLogin = false;

    profileStates[firstProfile.Id] = new Queue<ProfileRuntimeState>(
      new[]
      {
        new ProfileRuntimeState(MountLifecycleState.Mounted, MountHealthState.Healthy, firstTickAt, null),
      });
    profileStates[secondProfile.Id] = new Queue<ProfileRuntimeState>(
      new[]
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
    Func<MountProfile, CancellationToken, Task>? mountStartRunner = null,
    Func<MountProfile, CancellationToken, Task>? mountStopRunner = null,
    Func<MountProfile, CancellationToken, Task<bool>>? mountedProbe = null,
    Func<MountProfile, CancellationToken, Task<ProfileRuntimeState>>? runtimeStateVerifier = null,
    Func<TimeSpan, CancellationToken, Task<bool>>? runtimeRefreshWaiter = null)
  {
    async Task<IReadOnlyList<ProfileRuntimeState>> RuntimeStateBatchVerifier(
      IEnumerable<MountProfile> profiles,
      CancellationToken cancellationToken)
    {
      List<ProfileRuntimeState> states = new();
      foreach (MountProfile profile in profiles)
      {
        states.Add(
          await (runtimeStateVerifier?.Invoke(profile, cancellationToken)
                 ?? Task.FromResult(CreateState(MountLifecycleState.Idle, MountHealthState.Unknown))));
      }

      return states;
    }

    MainWindowViewModel viewModel = new(
      CreateProfilesPath(),
      mountStartRunner: mountStartRunner,
      mountStopRunner: mountStopRunner,
      mountedProbe: mountedProbe,
      runtimeStateVerifier: runtimeStateVerifier,
      startupEnabledProbe: _ => false,
      runtimeRefreshWaiter: runtimeRefreshWaiter,
      runtimeStateBatchVerifier: RuntimeStateBatchVerifier,
      loadStartupData: false,
      logger: TestLogger.CreateMainWindowViewModelLogger());

    _viewModels.Add(viewModel);
    return viewModel;
  }

  private string CreateProfilesPath()
  {
    Directory.CreateDirectory(_tempRoot);
    return Path.Combine(_tempRoot, "profiles.json");
  }

  private static ProfileRuntimeState CreateState(
    MountLifecycleState lifecycle,
    MountHealthState health,
    string? errorText = null)
  {
    return new ProfileRuntimeState(lifecycle, health, DateTimeOffset.UtcNow, errorText);
  }

  private static async Task WaitUntilAsync(Func<bool> condition)
  {
    for (int attempt = 0; attempt < 100; attempt++)
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