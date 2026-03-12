using RcloneMountManager.Core.Models;
using RcloneMountManager.GUI.ViewModels;

namespace RcloneMountManager.Tests.ViewModels;

public sealed class MainWindowViewModelSidebarSelectionTests : IDisposable
{
  private readonly string _tempRoot = Path.Combine(
    Path.GetTempPath(),
    $"main-window-sidebar-selection-tests-{Guid.NewGuid():N}");

  public void Dispose()
  {
    if (Directory.Exists(_tempRoot))
    {
      Directory.Delete(_tempRoot, true);
    }
  }

  [Fact]
  public void SelectingRemoteProfile_DoesNotOverwriteMountSelection()
  {
    MainWindowViewModel viewModel = CreateViewModel();
    viewModel.AddRemoteCommand.Execute(null);
    viewModel.AddMountCommand.Execute(null);

    MountProfile mountSelection = viewModel.MountProfiles[0];
    MountProfile remoteSelection = viewModel.RemoteProfiles[0];

    viewModel.SelectedMountProfile = mountSelection;
    viewModel.SelectedRemoteProfile = remoteSelection;

    Assert.Null(viewModel.SelectedMountProfile);
    Assert.Null(viewModel.SidebarSelectedMountProfile);
    Assert.Same(remoteSelection, viewModel.SidebarSelectedRemoteProfile);

    viewModel.ShowRemoteEditor = false;
    Assert.Same(mountSelection, viewModel.SelectedMountProfile);
  }

  [Fact]
  public void SelectingMountProfile_DoesNotOverwriteRemoteSelection()
  {
    MainWindowViewModel viewModel = CreateViewModel();
    viewModel.AddRemoteCommand.Execute(null);
    viewModel.AddMountCommand.Execute(null);

    MountProfile mountSelection = viewModel.MountProfiles[0];
    MountProfile remoteSelection = viewModel.RemoteProfiles[0];

    viewModel.SelectedRemoteProfile = remoteSelection;
    viewModel.SelectedMountProfile = mountSelection;

    Assert.Null(viewModel.SelectedRemoteProfile);
    Assert.Same(mountSelection, viewModel.SidebarSelectedMountProfile);
    Assert.Null(viewModel.SidebarSelectedRemoteProfile);

    viewModel.ShowRemoteEditor = true;
    Assert.Same(remoteSelection, viewModel.SelectedRemoteProfile);
  }

  [Fact]
  public void CrossClickingLists_AlwaysKeepsSingleActiveSidebarSelection()
  {
    MainWindowViewModel viewModel = CreateViewModel();
    viewModel.AddRemoteCommand.Execute(null);
    viewModel.AddMountCommand.Execute(null);

    MountProfile remoteSelection = viewModel.RemoteProfiles[0];
    MountProfile mountSelection = viewModel.MountProfiles[0];

    viewModel.SelectedRemoteProfile = remoteSelection;
    Assert.NotNull(viewModel.SidebarSelectedRemoteProfile);
    Assert.Null(viewModel.SidebarSelectedMountProfile);

    viewModel.SelectedMountProfile = mountSelection;
    Assert.NotNull(viewModel.SidebarSelectedMountProfile);
    Assert.Null(viewModel.SidebarSelectedRemoteProfile);

    viewModel.SelectedRemoteProfile = remoteSelection;
    Assert.NotNull(viewModel.SidebarSelectedRemoteProfile);
    Assert.Null(viewModel.SidebarSelectedMountProfile);
  }

  [Fact]
  public void AddRemoteAndAddMount_CreateSeparateEntities()
  {
    MainWindowViewModel viewModel = CreateViewModel();
    int initialMountCount = viewModel.MountProfiles.Count;
    int initialRemoteCount = viewModel.RemoteProfiles.Count;

    viewModel.AddRemoteCommand.Execute(null);
    viewModel.AddMountCommand.Execute(null);

    Assert.Equal(initialRemoteCount + 1, viewModel.RemoteProfiles.Count);
    Assert.Equal(initialMountCount + 1, viewModel.MountProfiles.Count);
    Assert.Contains(viewModel.Profiles, p => p.IsRemoteDefinition);
    Assert.Contains(viewModel.Profiles, p => !p.IsRemoteDefinition);
  }

  [Fact]
  public void AddCommands_AreAvailableAndMutateTheirOwnCollections()
  {
    MainWindowViewModel viewModel = CreateViewModel();

    Assert.True(viewModel.AddRemoteCommand.CanExecute(null));
    Assert.True(viewModel.AddMountCommand.CanExecute(null));

    int remoteCount = viewModel.RemoteProfiles.Count;
    int mountCount = viewModel.MountProfiles.Count;

    viewModel.AddRemoteCommand.Execute(null);
    Assert.Equal(remoteCount + 1, viewModel.RemoteProfiles.Count);
    Assert.Equal(mountCount, viewModel.MountProfiles.Count);

    viewModel.AddMountCommand.Execute(null);
    Assert.Equal(remoteCount + 1, viewModel.RemoteProfiles.Count);
    Assert.Equal(mountCount + 1, viewModel.MountProfiles.Count);
  }

  [Fact]
  public void MountWithoutRemoteAssociation_CannotBeSaved_UntilRemoteAssigned()
  {
    MainWindowViewModel viewModel = CreateViewModel();
    viewModel.AddMountCommand.Execute(null);

    MountProfile? newMount = viewModel.SelectedMountProfile;
    Assert.NotNull(newMount);
    Assert.False(viewModel.SaveChangesCommand.CanExecute(null));

    viewModel.AddRemoteCommand.Execute(null);
    viewModel.SelectedMountProfile = newMount;
    viewModel.SelectedMountRemoteProfile = viewModel.RemoteProfiles[0];

    Assert.True(viewModel.SaveChangesCommand.CanExecute(null));
    Assert.Contains(":", viewModel.SelectedMountProfile!.Source);
  }

  [Fact]
  public void RemoveRemote_BlocksWhenRemoteIsReferencedByMount()
  {
    MainWindowViewModel viewModel = CreateViewModel();
    MountProfile referencedRemote = viewModel.RemoteProfiles.First();
    string dependentMountName = viewModel.MountProfiles.First().Name;

    viewModel.SelectedProfile = referencedRemote;
    viewModel.RemoveProfileCommand.Execute(null);

    Assert.Contains(referencedRemote, viewModel.RemoteProfiles);
    Assert.True(viewModel.IsDeleteBlockedDialogVisible);
    Assert.Contains("Cannot delete remote", viewModel.DeleteBlockedDialogMessage, StringComparison.Ordinal);
    Assert.Contains(dependentMountName, viewModel.DeleteBlockedDialogMessage, StringComparison.Ordinal);
  }

  [Fact]
  public void RemoveRemote_AllowsDeletionWhenNotReferenced()
  {
    MainWindowViewModel viewModel = CreateViewModel();
    viewModel.AddRemoteCommand.Execute(null);
    MountProfile unreferencedRemote = viewModel.SelectedRemoteProfile!;
    int beforeCount = viewModel.RemoteProfiles.Count;

    viewModel.SelectedProfile = unreferencedRemote;
    viewModel.RemoveProfileCommand.Execute(null);

    Assert.Equal(beforeCount - 1, viewModel.RemoteProfiles.Count);
    Assert.DoesNotContain(unreferencedRemote, viewModel.RemoteProfiles);
  }

  [Fact]
  public void RemoveLastRemote_AfterRemovingMounts_ClearsAllRemotes()
  {
    MainWindowViewModel viewModel = CreateViewModel();
    MountProfile existingMount = viewModel.MountProfiles.First();
    MountProfile existingRemote = viewModel.RemoteProfiles.First();

    viewModel.SelectedProfile = existingMount;
    viewModel.RemoveProfileCommand.Execute(null);

    viewModel.SelectedProfile = existingRemote;
    viewModel.RemoveProfileCommand.Execute(null);

    Assert.Empty(viewModel.RemoteProfiles);
    Assert.Empty(viewModel.MountProfiles);
    Assert.Empty(viewModel.Profiles);
    Assert.False(viewModel.HasProfiles);
    Assert.Contains("Library is now empty", viewModel.StatusText, StringComparison.Ordinal);
  }

  [Fact]
  public void EditingRemoteNameField_UpdatesSidebarRemoteLabelImmediately()
  {
    MainWindowViewModel viewModel = CreateViewModel();
    MountProfile remote = viewModel.RemoteProfiles.First();

    viewModel.SelectedProfile = remote;
    viewModel.NewRemoteName = "archive-remote";

    Assert.Equal("archive-remote", remote.Name);
    Assert.Contains(viewModel.RemoteProfiles, p => p.Name == "archive-remote");

    viewModel.ShowRemoteEditor = false;
    viewModel.ShowRemoteEditor = true;

    Assert.Equal("archive-remote", viewModel.NewRemoteName);
    Assert.Equal("archive-remote", remote.Name);
  }

  [Fact]
  public void SelectingBackend_DoesNotOverrideEditedRemoteName()
  {
    MainWindowViewModel viewModel = CreateViewModel();
    MountProfile remote = viewModel.RemoteProfiles.First();
    viewModel.SelectedProfile = remote;
    viewModel.NewRemoteName = "stable-remote";

    viewModel.SelectedBackend = new RcloneBackendInfo {Name = "s3", Description = "Amazon S3"};

    Assert.Equal("stable-remote", viewModel.NewRemoteName);
    Assert.Equal("stable-remote", remote.Name);
  }

  [Fact]
  public void EmptyState_AfterClearingAllProfiles_RemainsStable()
  {
    MainWindowViewModel viewModel = CreateViewModel();
    MountProfile mount = viewModel.MountProfiles.First();
    MountProfile remote = viewModel.RemoteProfiles.First();

    viewModel.SelectedProfile = mount;
    viewModel.RemoveProfileCommand.Execute(null);
    viewModel.SelectedProfile = remote;
    viewModel.RemoveProfileCommand.Execute(null);

    Assert.False(viewModel.HasProfiles);
    Assert.False(viewModel.HasMountProfiles);
    Assert.False(viewModel.HasRemoteProfiles);
    Assert.False(viewModel.RemoveProfileCommand.CanExecute(null));
    Assert.False(viewModel.StartMountCommand.CanExecute(null));
  }

  [Fact]
  public void RemoteSidebarSubtitle_ShowsQuickConnectEndpoint()
  {
    MainWindowViewModel viewModel = CreateViewModel();
    MountProfile remote = viewModel.RemoteProfiles.First();

    Assert.False(remote.HasRemoteSidebarSubtitle);
    Assert.Equal(string.Empty, remote.RemoteSidebarSubtitle);

    remote.QuickConnectEndpoint = "sftp.example.com";

    Assert.True(remote.HasRemoteSidebarSubtitle);
    Assert.Equal("sftp.example.com", remote.RemoteSidebarSubtitle);
  }

  [Fact]
  public void RemoteSidebarSubtitle_FallsBackToBackendOptionUrl()
  {
    MainWindowViewModel viewModel = CreateViewModel();
    MountProfile remote = viewModel.RemoteProfiles.First();

    remote.BackendOptions = new Dictionary<string, string> {["url"] = "https://cloud.example.com"};

    Assert.True(remote.HasRemoteSidebarSubtitle);
    Assert.Equal("https://cloud.example.com", remote.RemoteSidebarSubtitle);
  }

  [Fact]
  public void CreateMountAndRemote_SaveAndReload_PreservesState()
  {
    string filePath = Path.Combine(_tempRoot, $"profiles-{Guid.NewGuid():N}.json");
    MainWindowViewModel viewModel = CreateViewModel(filePath);

    viewModel.AddRemoteCommand.Execute(null);
    MountProfile remote = viewModel.SelectedRemoteProfile!;
    remote.Name = "media-remote";
    remote.Source = "media-remote:/";
    viewModel.AddMountCommand.Execute(null);
    viewModel.SelectedMountRemoteProfile = remote;
    Assert.True(viewModel.SaveChangesCommand.CanExecute(null));
    viewModel.SaveChangesCommand.Execute(null);

    MainWindowViewModel reloaded = CreateViewModel(filePath);
    Assert.Contains(reloaded.RemoteProfiles, p => p.Name == "media-remote");
    Assert.NotEmpty(reloaded.MountProfiles);
    Assert.Contains(reloaded.MountProfiles, m => m.Source.StartsWith("media-remote:", StringComparison.Ordinal));
  }

  [Fact]
  public void RenameRemote_SaveAndReload_PreservesName()
  {
    string filePath = Path.Combine(_tempRoot, $"profiles-{Guid.NewGuid():N}.json");
    MainWindowViewModel viewModel = CreateViewModel(filePath);
    viewModel.SelectedProfile = viewModel.MountProfiles.First();
    viewModel.RemoveProfileCommand.Execute(null);
    MountProfile remote = viewModel.RemoteProfiles.First();

    viewModel.SelectedProfile = remote;
    viewModel.NewRemoteName = "archive-remote";
    Assert.True(viewModel.SaveChangesCommand.CanExecute(null));
    viewModel.SaveChangesCommand.Execute(null);

    MainWindowViewModel reloaded = CreateViewModel(filePath);
    Assert.Contains(reloaded.RemoteProfiles, p => p.Name == "archive-remote");
  }

  [Fact]
  public void ClearAll_SaveAndReload_RemainsEmpty()
  {
    string filePath = Path.Combine(_tempRoot, $"profiles-{Guid.NewGuid():N}.json");
    MainWindowViewModel viewModel = CreateViewModel(filePath);

    viewModel.SelectedProfile = viewModel.MountProfiles.First();
    viewModel.RemoveProfileCommand.Execute(null);
    viewModel.SelectedProfile = viewModel.RemoteProfiles.First();
    viewModel.RemoveProfileCommand.Execute(null);

    MainWindowViewModel reloaded = CreateViewModel(filePath);
    Assert.Empty(reloaded.Profiles);
    Assert.Empty(reloaded.RemoteProfiles);
    Assert.Empty(reloaded.MountProfiles);
  }

  [Fact]
  public void RenameRemote_UpdatesDefaultGeneratedMountSourceAlias()
  {
    MainWindowViewModel viewModel = CreateViewModel();
    MountProfile remote = viewModel.RemoteProfiles.First();
    MountProfile mount = viewModel.MountProfiles.First();
    mount.Source = "remote:/";

    viewModel.SelectedProfile = remote;
    viewModel.NewRemoteName = "renamed-remote";

    Assert.Equal("renamed-remote:/", mount.Source);
  }

  [Fact]
  public void RenameRemote_DoesNotOverwriteCustomMountSourcePath()
  {
    MainWindowViewModel viewModel = CreateViewModel();
    MountProfile remote = viewModel.RemoteProfiles.First();
    MountProfile mount = viewModel.MountProfiles.First();
    mount.Source = "remote:media/photos";

    viewModel.SelectedProfile = remote;
    viewModel.NewRemoteName = "renamed-remote";

    Assert.Equal("remote:media/photos", mount.Source);
  }

  private MainWindowViewModel CreateViewModel(string? filePath = null)
  {
    Directory.CreateDirectory(_tempRoot);
    return new MainWindowViewModel(
      filePath ?? Path.Combine(_tempRoot, "profiles.json"),
      startupEnabledProbe: _ => false,
      loadStartupData: false);
  }
}