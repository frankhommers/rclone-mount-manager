using RcloneMountManager.Core.Models;
using RcloneMountManager.GUI.ViewModels;

namespace RcloneMountManager.Tests.ViewModels;

public sealed class MainWindowViewModelDashboardCardTests : IDisposable
{
  private readonly string _tempRoot = Path.Combine(
    Path.GetTempPath(),
    $"main-window-dashboard-card-tests-{Guid.NewGuid():N}");

  public void Dispose()
  {
    if (Directory.Exists(_tempRoot))
    {
      Directory.Delete(_tempRoot, true);
    }
  }

  [Fact]
  public void DashboardMountCards_ContainOnlyMountProfiles()
  {
    MainWindowViewModel viewModel = CreateViewModel();

    Assert.Equal(viewModel.MountProfiles.Count, viewModel.DashboardMountCards.Count);
    Assert.All(viewModel.DashboardMountCards, card => Assert.False(card.Profile.IsRemoteDefinition));
  }

  [Fact]
  public void DashboardCardNavigateCommand_SelectsProfileAndHidesDashboard()
  {
    MainWindowViewModel viewModel = CreateViewModel();
    viewModel.SelectDashboardCommand.Execute(null);

    DashboardMountCardViewModel card = viewModel.DashboardMountCards.First();

    card.NavigateToMountCommand.Execute(null);

    Assert.Same(card.Profile, viewModel.SelectedProfile);
    Assert.False(viewModel.ShowDashboard);
  }

  [Fact]
  public async Task DashboardCardStartCommand_StartsTheCardProfile()
  {
    MountProfile? startedProfile = null;
    MainWindowViewModel viewModel = CreateViewModel((profile, _) =>
    {
      startedProfile = profile;
      return Task.CompletedTask;
    });

    DashboardMountCardViewModel card = viewModel.DashboardMountCards.First();

    await card.StartMountCommand.ExecuteAsync(null);

    Assert.Same(card.Profile, startedProfile);
    Assert.Same(card.Profile, viewModel.SelectedProfile);
  }

  [Fact]
  public void DashboardCardEditMountCommand_OpensConfigurationModeForMount()
  {
    MainWindowViewModel viewModel = CreateViewModel();
    viewModel.SelectDashboardCommand.Execute(null);

    DashboardMountCardViewModel card = viewModel.DashboardMountCards.First();

    card.EditMountCommand.Execute(null);

    Assert.Same(card.Profile, viewModel.SelectedProfile);
    Assert.False(viewModel.ShowDashboard);
    Assert.True(viewModel.IsConfigurationMode);
    Assert.True(viewModel.ShowMountConfigContent);
  }

  [Fact]
  public void DashboardCardEditRemoteCommand_OpensLinkedRemoteEditor()
  {
    MainWindowViewModel viewModel = CreateViewModel();
    viewModel.SelectDashboardCommand.Execute(null);

    DashboardMountCardViewModel card = viewModel.DashboardMountCards.First();

    Assert.True(card.HasLinkedRemote);
    card.EditRemoteCommand.Execute(null);

    Assert.NotNull(viewModel.SelectedRemoteProfile);
    Assert.True(viewModel.SelectedRemoteProfile.IsRemoteDefinition);
    Assert.True(viewModel.ShowRemoteEditor);
    Assert.False(viewModel.ShowDashboard);
  }

  [Fact]
  public void DashboardCardHasLinkedRemote_IsFalse_WhenRemoteCannotBeResolved()
  {
    MainWindowViewModel viewModel = CreateViewModel();
    viewModel.AddMountCommand.Execute(null);
    MountProfile mountWithoutRemote = viewModel.SelectedMountProfile!;
    mountWithoutRemote.Source = "missing-alias:/";

    DashboardMountCardViewModel card =
      viewModel.DashboardMountCards.First(c => ReferenceEquals(c.Profile, mountWithoutRemote));

    Assert.False(card.HasLinkedRemote);
    Assert.False(card.EditRemoteCommand.CanExecute(null));
  }

  [Fact]
  public void RevealInFileManagerCommand_IsExecutable_FromDashboardForMountedProfile()
  {
    MainWindowViewModel viewModel = CreateViewModel();
    viewModel.SelectDashboardCommand.Execute(null);

    DashboardMountCardViewModel card = viewModel.DashboardMountCards.First();
    string mountPoint = Path.Combine(_tempRoot, "mounted-dashboard-card");
    Directory.CreateDirectory(mountPoint);

    card.Profile.MountPoint = mountPoint;
    card.Profile.RuntimeState = new ProfileRuntimeState(
      MountLifecycleState.Mounted,
      MountHealthState.Healthy,
      DateTimeOffset.UtcNow,
      null);

    viewModel.SelectedProfile = card.Profile;

    Assert.True(viewModel.ShowDashboard);
    Assert.True(viewModel.RevealInFileManagerCommand.CanExecute(null));
  }

  [Fact]
  public void RevealInFileManagerLabel_IsPlatformSpecific()
  {
    MainWindowViewModel viewModel = CreateViewModel();
    DashboardMountCardViewModel card = viewModel.DashboardMountCards.First();

    if (OperatingSystem.IsWindows())
    {
      Assert.Equal("Show in File Explorer", card.RevealInFileManagerLabel);
      Assert.Equal("Show in File Explorer", viewModel.RevealInFileManagerLabel);
      return;
    }

    if (OperatingSystem.IsMacOS())
    {
      Assert.Equal("Reveal in Finder", card.RevealInFileManagerLabel);
      Assert.Equal("Reveal in Finder", viewModel.RevealInFileManagerLabel);
      return;
    }

    Assert.Equal("Show in File Manager", card.RevealInFileManagerLabel);
    Assert.Equal("Show in File Manager", viewModel.RevealInFileManagerLabel);
  }

  private MainWindowViewModel CreateViewModel(
    Func<MountProfile, CancellationToken, Task>? mountStartRunner = null)
  {
    Directory.CreateDirectory(_tempRoot);

    return new MainWindowViewModel(
      Path.Combine(_tempRoot, "profiles.json"),
      mountStartRunner: mountStartRunner,
      startupEnabledProbe: _ => false,
      loadStartupData: false);
  }
}
