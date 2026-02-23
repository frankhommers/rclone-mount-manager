using RcloneMountManager.Core.Models;
using RcloneMountManager.ViewModels;

namespace RcloneMountManager.Tests.ViewModels;

public sealed class MainWindowViewModelTestDialogTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"test-dialog-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task TestConnection_OpensDialogAndShowsSuccess()
    {
        var vm = CreateViewModel(
            testConnectionRunner: (profile, log, _) =>
            {
                log("Listing objects...");
                log("Connectivity test succeeded.");
                return Task.CompletedTask;
            });

        var profile = vm.SelectedProfile;
        profile.Source = "myremote:bucket";

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.False(vm.IsTestDialogRunning);
        Assert.True(vm.TestDialogSuccess);
        Assert.True(vm.IsTestDialogVisible);
        Assert.Equal("Connection test passed", vm.TestDialogTitle);
        Assert.Contains(vm.TestDialogLines, l => l.Contains("Connectivity test succeeded."));
    }

    [Fact]
    public async Task TestConnection_OpensDialogAndShowsFailure()
    {
        var vm = CreateViewModel(
            testConnectionRunner: (profile, log, _) =>
            {
                log("ERR: couldn't connect");
                throw new InvalidOperationException("Connectivity test failed with exit code 1.");
            });

        var profile = vm.SelectedProfile;
        profile.Source = "myremote:bucket";

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.False(vm.IsTestDialogRunning);
        Assert.False(vm.TestDialogSuccess);
        Assert.True(vm.IsTestDialogVisible);
        Assert.Equal("Connection test failed", vm.TestDialogTitle);
        Assert.Contains(vm.TestDialogLines, l => l.Contains("exit code 1"));
    }

    [Fact]
    public async Task DismissTestDialog_ClearsState()
    {
        var vm = CreateViewModel(
            testConnectionRunner: (_, log, _) =>
            {
                log("OK");
                return Task.CompletedTask;
            });

        vm.SelectedProfile.Source = "myremote:bucket";
        await vm.TestConnectionCommand.ExecuteAsync(null);
        Assert.True(vm.IsTestDialogVisible);

        vm.DismissTestDialogCommand.Execute(null);

        Assert.False(vm.IsTestDialogVisible);
        Assert.Empty(vm.TestDialogLines);
        Assert.Null(vm.TestDialogSuccess);
    }

    private MainWindowViewModel CreateViewModel(
        Func<MountProfile, Action<string>, CancellationToken, Task>? testConnectionRunner = null)
    {
        return new MainWindowViewModel(
            profilesFilePath: CreateProfilesPath(),
            mountStartRunner: (_, _, _) => Task.CompletedTask,
            testConnectionRunner: testConnectionRunner,
            runtimeStateVerifier: (_, _) => Task.FromResult(
                new ProfileRuntimeState(MountLifecycleState.Idle, MountHealthState.Unknown, DateTimeOffset.UtcNow, null)),
            startupEnabledProbe: _ => false,
            runtimeRefreshWaiter: (_, _) => Task.FromResult(false),
            runtimeStateBatchVerifier: (_, _) => Task.FromResult<IReadOnlyList<ProfileRuntimeState>>(Array.Empty<ProfileRuntimeState>()),
            loadStartupData: false);
    }

    private string CreateProfilesPath()
    {
        Directory.CreateDirectory(_tempRoot);
        return Path.Combine(_tempRoot, "profiles.json");
    }
}
