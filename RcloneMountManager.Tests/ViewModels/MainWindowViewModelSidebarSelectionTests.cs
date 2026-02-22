using RcloneMountManager.ViewModels;
using RcloneMountManager.Core.Models;

namespace RcloneMountManager.Tests.ViewModels;

public sealed class MainWindowViewModelSidebarSelectionTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"main-window-sidebar-selection-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void SelectingRemoteProfile_DoesNotOverwriteMountSelection()
    {
        var viewModel = CreateViewModel();
        viewModel.AddRemoteCommand.Execute(null);
        viewModel.AddMountCommand.Execute(null);

        var mountSelection = viewModel.MountProfiles[0];
        var remoteSelection = viewModel.RemoteProfiles[0];

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
        var viewModel = CreateViewModel();
        viewModel.AddRemoteCommand.Execute(null);
        viewModel.AddMountCommand.Execute(null);

        var mountSelection = viewModel.MountProfiles[0];
        var remoteSelection = viewModel.RemoteProfiles[0];

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
        var viewModel = CreateViewModel();
        viewModel.AddRemoteCommand.Execute(null);
        viewModel.AddMountCommand.Execute(null);

        var remoteSelection = viewModel.RemoteProfiles[0];
        var mountSelection = viewModel.MountProfiles[0];

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
        var viewModel = CreateViewModel();
        var initialMountCount = viewModel.MountProfiles.Count;
        var initialRemoteCount = viewModel.RemoteProfiles.Count;

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
        var viewModel = CreateViewModel();

        Assert.True(viewModel.AddRemoteCommand.CanExecute(null));
        Assert.True(viewModel.AddMountCommand.CanExecute(null));

        var remoteCount = viewModel.RemoteProfiles.Count;
        var mountCount = viewModel.MountProfiles.Count;

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
        var viewModel = CreateViewModel();
        viewModel.AddMountCommand.Execute(null);

        var newMount = viewModel.SelectedMountProfile;
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
        var viewModel = CreateViewModel();
        var referencedRemote = viewModel.RemoteProfiles.First();
        var dependentMountName = viewModel.MountProfiles.First().Name;

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
        var viewModel = CreateViewModel();
        viewModel.AddRemoteCommand.Execute(null);
        var unreferencedRemote = viewModel.SelectedRemoteProfile!;
        var beforeCount = viewModel.RemoteProfiles.Count;

        viewModel.SelectedProfile = unreferencedRemote;
        viewModel.RemoveProfileCommand.Execute(null);

        Assert.Equal(beforeCount - 1, viewModel.RemoteProfiles.Count);
        Assert.DoesNotContain(unreferencedRemote, viewModel.RemoteProfiles);
    }

    [Fact]
    public void RemoveLastRemote_AfterRemovingMounts_ClearsAllRemotes()
    {
        var viewModel = CreateViewModel();
        var existingMount = viewModel.MountProfiles.First();
        var existingRemote = viewModel.RemoteProfiles.First();

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
        var viewModel = CreateViewModel();
        var remote = viewModel.RemoteProfiles.First();

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
        var viewModel = CreateViewModel();
        var remote = viewModel.RemoteProfiles.First();
        viewModel.SelectedProfile = remote;
        viewModel.NewRemoteName = "stable-remote";

        viewModel.SelectedBackend = new RcloneBackendInfo { Name = "s3", Description = "Amazon S3" };

        Assert.Equal("stable-remote", viewModel.NewRemoteName);
        Assert.Equal("stable-remote", remote.Name);
    }

    [Fact]
    public void EmptyState_AfterClearingAllProfiles_RemainsStable()
    {
        var viewModel = CreateViewModel();
        var mount = viewModel.MountProfiles.First();
        var remote = viewModel.RemoteProfiles.First();

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
    public void RemoteSidebarSubtitle_HidesAliasRootPlaceholder_AndShowsMeaningfulTarget()
    {
        var viewModel = CreateViewModel();
        var remote = viewModel.RemoteProfiles.First();

        Assert.False(remote.HasRemoteSidebarSubtitle);
        Assert.Equal(string.Empty, remote.RemoteSidebarSubtitle);

        remote.Source = "archive:photos";

        Assert.True(remote.HasRemoteSidebarSubtitle);
        Assert.Equal("Target: photos", remote.RemoteSidebarSubtitle);
    }

    private MainWindowViewModel CreateViewModel()
    {
        Directory.CreateDirectory(_tempRoot);
        return new MainWindowViewModel(
            profilesFilePath: Path.Combine(_tempRoot, "profiles.json"),
            startupEnabledProbe: _ => false,
            loadStartupData: false);
    }
}
