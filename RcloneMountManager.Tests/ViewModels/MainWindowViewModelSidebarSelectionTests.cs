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
        viewModel.AddProfileCommand.Execute(null);

        var mountSelection = viewModel.Profiles[0];
        var remoteSelection = viewModel.Profiles[1];

        viewModel.SelectedMountProfile = mountSelection;
        viewModel.SelectedRemoteProfile = remoteSelection;

        Assert.Same(mountSelection, viewModel.SelectedMountProfile);
    }

    [Fact]
    public void SelectingMountProfile_DoesNotOverwriteRemoteSelection()
    {
        var viewModel = CreateViewModel();
        viewModel.AddProfileCommand.Execute(null);

        var mountSelection = viewModel.Profiles[0];
        var remoteSelection = viewModel.Profiles[1];

        viewModel.SelectedRemoteProfile = remoteSelection;
        viewModel.SelectedMountProfile = mountSelection;

        Assert.Same(remoteSelection, viewModel.SelectedRemoteProfile);
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
