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
    public async Task StartMountCommand_KeepsAsyncLogAttributionWhenSelectedProfileChanges()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = CreateViewModel(
            mountStartRunner: async (profile, log, _) =>
            {
                await gate.Task;
                log($"runner callback for {profile.Id}");
            },
            runtimeStateVerifier: (_, _) => Task.FromResult(CreateState(MountLifecycleState.Mounted, MountHealthState.Healthy)));

        var firstProfile = viewModel.SelectedProfile;
        viewModel.AddProfileCommand.Execute(null);
        var secondProfile = viewModel.SelectedProfile;

        viewModel.SelectedProfile = firstProfile;
        var startTask = viewModel.StartMountCommand.ExecuteAsync(null);

        await WaitUntilAsync(() => viewModel.IsBusy);
        viewModel.SelectedProfile = secondProfile;

        gate.SetResult();
        await startTask;

        Assert.DoesNotContain(viewModel.Logs, entry => entry.Contains("runner callback", StringComparison.OrdinalIgnoreCase));

        viewModel.SelectedProfile = firstProfile;
        Assert.Contains(viewModel.Logs, entry => entry.Contains("runner callback", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TypedDiagnosticsStorage_IsBoundedPerProfileWithoutCrossProfileBleed()
    {
        var viewModel = CreateViewModel(
            mountStartRunner: (profile, log, _) =>
            {
                for (var index = 0; index < 400; index++)
                {
                    log($"{profile.Id}-entry-{index}");
                }

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

        viewModel.SelectedProfile = firstProfile;
        Assert.True(viewModel.Logs.Count <= 250);
        Assert.Contains(viewModel.Logs, entry => entry.Contains($"{firstProfile.Id}-entry-399", StringComparison.Ordinal));
        Assert.DoesNotContain(viewModel.Logs, entry => entry.Contains(secondProfile.Id, StringComparison.Ordinal));

        viewModel.SelectedProfile = secondProfile;
        Assert.True(viewModel.Logs.Count <= 250);
        Assert.Contains(viewModel.Logs, entry => entry.Contains($"{secondProfile.Id}-entry-399", StringComparison.Ordinal));
        Assert.DoesNotContain(viewModel.Logs, entry => entry.Contains(firstProfile.Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task InitializeRuntimeMonitoring_EmitsStartupCategoryVerificationEventsForEachStartAtLoginProfile()
    {
        var viewModel = CreateViewModel(
            runtimeStateVerifier: (_, _) => Task.FromResult(CreateState(MountLifecycleState.Mounted, MountHealthState.Healthy)),
            runtimeRefreshWaiter: (_, _) => Task.FromResult(false));

        var firstStartupProfile = viewModel.SelectedProfile;
        firstStartupProfile.StartAtLogin = true;

        viewModel.AddProfileCommand.Execute(null);
        var secondStartupProfile = viewModel.SelectedProfile;
        secondStartupProfile.StartAtLogin = true;

        viewModel.AddProfileCommand.Execute(null);
        var nonStartupProfile = viewModel.SelectedProfile;
        nonStartupProfile.StartAtLogin = false;

        viewModel.InitializeRuntimeMonitoring();

        await WaitUntilAsync(() =>
            firstStartupProfile.RuntimeState.Health is MountHealthState.Healthy &&
            secondStartupProfile.RuntimeState.Health is MountHealthState.Healthy);

        viewModel.SelectedProfile = firstStartupProfile;
        Assert.Contains(viewModel.Logs, entry => entry.Contains("[startup/verification]", StringComparison.OrdinalIgnoreCase));

        viewModel.SelectedProfile = secondStartupProfile;
        Assert.Contains(viewModel.Logs, entry => entry.Contains("[startup/verification]", StringComparison.OrdinalIgnoreCase));

        viewModel.SelectedProfile = nonStartupProfile;
        Assert.DoesNotContain(viewModel.Logs, entry => entry.Contains("Startup verification:", StringComparison.OrdinalIgnoreCase));

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
