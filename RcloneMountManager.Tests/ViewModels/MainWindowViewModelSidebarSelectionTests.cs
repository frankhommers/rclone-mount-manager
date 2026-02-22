using RcloneMountManager.ViewModels;

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

        Assert.Same(remoteSelection, viewModel.SelectedRemoteProfile);
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

    private MainWindowViewModel CreateViewModel()
    {
        Directory.CreateDirectory(_tempRoot);
        return new MainWindowViewModel(
            profilesFilePath: Path.Combine(_tempRoot, "profiles.json"),
            startupEnabledProbe: _ => false,
            loadStartupData: false);
    }
}
