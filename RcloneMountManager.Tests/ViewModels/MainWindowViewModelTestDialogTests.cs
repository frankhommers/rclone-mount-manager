using RcloneMountManager.Core.Models;
using RcloneMountManager.GUI.ViewModels;

namespace RcloneMountManager.Tests.ViewModels;

public sealed class MainWindowViewModelTestDialogTests : IDisposable
{
  private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"test-dialog-tests-{Guid.NewGuid():N}");
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
  public async Task TestConnection_OpensDialogAndShowsSuccess()
  {
    MainWindowViewModel vm = CreateViewModel((profile, _) => { return Task.CompletedTask; });

    MountProfile profile = vm.SelectedProfile;
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
    MainWindowViewModel vm = CreateViewModel((profile, _) =>
    {
      throw new InvalidOperationException(
        "Connectivity test failed with exit code 1.");
    });

    MountProfile profile = vm.SelectedProfile;
    profile.Source = "myremote:bucket";

    await vm.TestConnectionCommand.ExecuteAsync(null);

    Assert.False(vm.IsTestDialogRunning);
    Assert.False(vm.TestDialogSuccess);
    Assert.True(vm.IsTestDialogVisible);
    Assert.Equal("Connection test failed", vm.TestDialogTitle);
    Assert.Contains(vm.TestDialogLines, l => l.Contains("exit code 1"));
  }

  [Theory]
  [InlineData(MountType.RcloneNfs)]
  [InlineData(MountType.RcloneFuse)]
  public async Task TestConnection_WorksForAllRcloneMountTypes(MountType mountType)
  {
    MainWindowViewModel vm = CreateViewModel((profile, _) => { return Task.CompletedTask; });

    MountProfile profile = vm.SelectedProfile;
    profile.Source = "myremote:bucket";
    profile.Type = mountType;

    await vm.TestConnectionCommand.ExecuteAsync(null);

    Assert.True(vm.TestDialogSuccess);
    Assert.Equal("Connection test passed", vm.TestDialogTitle);
  }

  [Fact]
  public async Task DismissTestDialog_ClearsState()
  {
    MainWindowViewModel vm = CreateViewModel((_, _) => { return Task.CompletedTask; });

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
    MainWindowViewModel viewModel = new(
      CreateProfilesPath(),
      mountStartRunner: (_, _) => Task.CompletedTask,
      testConnectionRunner: testConnectionRunner,
      runtimeStateVerifier: (_, _) => Task.FromResult(
        new ProfileRuntimeState(MountLifecycleState.Idle, MountHealthState.Unknown, DateTimeOffset.UtcNow, null)),
      startupEnabledProbe: _ => false,
      runtimeRefreshWaiter: (_, _) => Task.FromResult(false),
      runtimeStateBatchVerifier: (_, _) =>
        Task.FromResult<IReadOnlyList<ProfileRuntimeState>>(Array.Empty<ProfileRuntimeState>()),
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