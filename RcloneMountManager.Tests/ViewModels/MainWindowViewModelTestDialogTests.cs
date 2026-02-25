using RcloneMountManager.Core.Models;
using RcloneMountManager.ViewModels;

namespace RcloneMountManager.Tests.ViewModels;

public sealed class MainWindowViewModelTestDialogTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"test-dialog-tests-{Guid.NewGuid():N}");
    private readonly List<MainWindowViewModel> _viewModels = [];

    public void Dispose()
    {
        foreach (var viewModel in _viewModels)
        {
            viewModel.Dispose();
        }

        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task TestConnection_OpensDialogAndShowsSuccess()
    {
        var vm = CreateViewModel(
            testConnectionRunner: (profile, _) =>
            {
                return Task.CompletedTask;
            });

        var profile = vm.SelectedProfile;
        profile.Source = "myremote:bucket";

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.False(vm.IsTestDialogRunning);
        Assert.True(vm.TestDialogSuccess);
        Assert.True(vm.IsTestDialogVisible);
        Assert.Equal("Connection test passed", vm.TestDialogTitle);
        Assert.Contains(vm.TestDialogLines, l => l.Contains("Testing connection"));
    }

    [Fact]
    public async Task TestConnection_OpensDialogAndShowsFailure()
    {
        var vm = CreateViewModel(
            testConnectionRunner: (profile, _) =>
            {
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
            testConnectionRunner: (_, _) =>
            {
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
        Func<MountProfile, CancellationToken, Task>? testConnectionRunner = null)
    {
        var viewModel = new MainWindowViewModel(
            profilesFilePath: CreateProfilesPath(),
            mountStartRunner: (_, _) => Task.CompletedTask,
            testConnectionRunner: testConnectionRunner,
            runtimeStateVerifier: (_, _) => Task.FromResult(
                new ProfileRuntimeState(MountLifecycleState.Idle, MountHealthState.Unknown, DateTimeOffset.UtcNow, null)),
            startupEnabledProbe: _ => false,
            runtimeRefreshWaiter: (_, _) => Task.FromResult(false),
            runtimeStateBatchVerifier: (_, _) => Task.FromResult<IReadOnlyList<ProfileRuntimeState>>(Array.Empty<ProfileRuntimeState>()),
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
}
