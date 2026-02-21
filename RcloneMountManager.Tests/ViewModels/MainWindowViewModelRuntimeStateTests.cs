using RcloneMountManager.Core.Models;
using RcloneMountManager.ViewModels;

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

    private MainWindowViewModel CreateViewModel(
        Func<MountProfile, Action<string>, CancellationToken, Task>? mountStartRunner = null,
        Func<MountProfile, Action<string>, CancellationToken, Task>? mountStopRunner = null,
        Func<MountProfile, CancellationToken, Task<bool>>? mountedProbe = null,
        Func<MountProfile, CancellationToken, Task<ProfileRuntimeState>>? runtimeStateVerifier = null)
    {
        return new MainWindowViewModel(
            profilesFilePath: CreateProfilesPath(),
            mountStartRunner: mountStartRunner,
            mountStopRunner: mountStopRunner,
            mountedProbe: mountedProbe,
            runtimeStateVerifier: runtimeStateVerifier,
            startupEnabledProbe: _ => false,
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
