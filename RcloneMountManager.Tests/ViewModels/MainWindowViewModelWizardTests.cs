using System.Collections.ObjectModel;
using RcloneMountManager.Core.Models;
using RcloneMountManager.GUI.ViewModels;

namespace RcloneMountManager.Tests.ViewModels;

public class MainWindowViewModelWizardTests : IDisposable
{
  private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"wizard-tests-{Guid.NewGuid():N}");
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

  private MainWindowViewModel CreateViewModel()
  {
    MainWindowViewModel viewModel = new(
      profilesFilePath: Path.Combine(_tempRoot, "profiles.json"),
      startupEnabledProbe: _ => false,
      sourceRemoteExistsInRcloneConfigRunner: (_, _, _) => Task.FromResult(true),
      loadStartupData: false);
    _viewModels.Add(viewModel);
    return viewModel;
  }

  [Fact]
  public void IsWizardActive_DefaultsFalse()
  {
    MainWindowViewModel vm = CreateViewModel();
    Assert.False(vm.IsWizardActive);
  }

  [Fact]
  public void ShowWizardContent_FalseWhenWizardInactive()
  {
    MainWindowViewModel vm = CreateViewModel();
    Assert.False(vm.ShowWizardContent);
  }

  [Fact]
  public void ShowStandardRemoteForm_TrueWhenRemoteEditorActiveAndWizardInactive()
  {
    MainWindowViewModel vm = CreateViewModel();
    vm.ShowRemoteEditor = true;
    Assert.True(vm.ShowStandardRemoteForm);
  }

  [Fact]
  public void ShowStandardRemoteForm_FalseWhenWizardActive()
  {
    MainWindowViewModel vm = CreateViewModel();
    vm.ShowRemoteEditor = true;
    vm.IsWizardActive = true;
    Assert.False(vm.ShowStandardRemoteForm);
  }

  [Fact]
  public void WizardStepTitle_EmptyWhenNoStep()
  {
    MainWindowViewModel vm = CreateViewModel();
    Assert.Empty(vm.WizardStepTitle);
  }

  [Fact]
  public void WizardStepHelp_EmptyWhenNoStep()
  {
    MainWindowViewModel vm = CreateViewModel();
    Assert.Empty(vm.WizardStepHelp);
  }

  [Fact]
  public void WizardStepHelp_ReplacesNewLinesWithSpaces()
  {
    MainWindowViewModel vm = CreateViewModel();
    vm.CurrentWizardStep = new ConfigWizardStep
    {
      Name = "provider",
      Help = "line one\nline two",
      State = "x",
    };

    Assert.Equal("line one line two", vm.WizardStepHelp);
  }

  [Fact]
  public void WizardHasExamples_FalseWhenNoStep()
  {
    MainWindowViewModel vm = CreateViewModel();
    Assert.False(vm.WizardHasExamples);
  }

  [Fact]
  public void WizardHasExamples_TrueWhenExclusiveAndExamplesExist()
  {
    MainWindowViewModel vm = CreateViewModel();
    vm.CurrentWizardStep = new ConfigWizardStep
    {
      Name = "provider",
      State = "x",
      Exclusive = true,
      Examples =
      [
        new ConfigWizardExample {Value = "drive", Help = "Google Drive"},
      ],
    };

    Assert.True(vm.WizardHasExamples);
  }

  [Fact]
  public void WizardHasExamples_FalseWhenExamplesExistButNotExclusive()
  {
    MainWindowViewModel vm = CreateViewModel();
    vm.CurrentWizardStep = new ConfigWizardStep
    {
      Name = "provider",
      State = "x",
      Exclusive = false,
      Examples =
      [
        new ConfigWizardExample {Value = "drive", Help = "Google Drive"},
      ],
    };

    Assert.False(vm.WizardHasExamples);
  }

  [Fact]
  public async Task CancelWizard_ResetsState()
  {
    MainWindowViewModel vm = CreateViewModel();
    vm.IsWizardActive = true;
    vm.WizardAnswer = "test";
    vm.WizardOAuthUrl = "http://example.com";
    vm.WizardStepNumber = 3;

    await vm.CancelWizardCommand.ExecuteAsync(null);

    Assert.False(vm.IsWizardActive);
    Assert.Empty(vm.WizardAnswer);
    Assert.Empty(vm.WizardOAuthUrl);
    Assert.Equal(0, vm.WizardStepNumber);
  }

  [Fact]
  public void ShowRemoteChooser_TrueWhenRemoteEditorActiveAndNeitherWizardNorManual()
  {
    MainWindowViewModel vm = CreateViewModel();
    vm.ShowRemoteEditor = true;
    Assert.True(vm.ShowRemoteChooser);
  }

  [Fact]
  public void ShowRemoteChooser_FalseWhenWizardActive()
  {
    MainWindowViewModel vm = CreateViewModel();
    vm.ShowRemoteEditor = true;
    vm.IsWizardActive = true;
    Assert.False(vm.ShowRemoteChooser);
  }

  [Fact]
  public void ShowRemoteChooser_FalseWhenManualMode()
  {
    MainWindowViewModel vm = CreateViewModel();
    vm.ShowRemoteEditor = true;
    vm.IsManualMode = true;
    Assert.False(vm.ShowRemoteChooser);
  }

  [Fact]
  public void ShowManualRemoteForm_TrueWhenManualModeAndRemoteEditorActive()
  {
    MainWindowViewModel vm = CreateViewModel();
    vm.ShowRemoteEditor = true;
    vm.IsManualMode = true;
    Assert.True(vm.ShowManualRemoteForm);
  }

  [Fact]
  public void ShowManualRemoteForm_FalseWhenNotManualMode()
  {
    MainWindowViewModel vm = CreateViewModel();
    vm.ShowRemoteEditor = true;
    Assert.False(vm.ShowManualRemoteForm);
  }

  [Fact]
  public void EnterManualMode_SetsIsManualModeTrue()
  {
    MainWindowViewModel vm = CreateViewModel();
    vm.ShowRemoteEditor = true;
    vm.EnterManualModeCommand.Execute(null);
    Assert.True(vm.IsManualMode);
    Assert.True(vm.ShowManualRemoteForm);
    Assert.False(vm.ShowRemoteChooser);
  }

  [Fact]
  public void ExitManualMode_SetsIsManualModeFalse()
  {
    MainWindowViewModel vm = CreateViewModel();
    vm.ShowRemoteEditor = true;
    vm.IsManualMode = true;
    vm.ExitManualModeCommand.Execute(null);
    Assert.False(vm.IsManualMode);
    Assert.True(vm.ShowRemoteChooser);
    Assert.False(vm.ShowManualRemoteForm);
  }

  [Fact]
  public void SwitchingRemoteProfile_RestoresPerProfileMode()
  {
    MainWindowViewModel vm = CreateViewModel();
    vm.AddRemoteCommand.Execute(null);
    vm.AddRemoteCommand.Execute(null);
    vm.ShowRemoteEditor = true;

    ObservableCollection<MountProfile> remoteProfiles = vm.RemoteProfiles;
    Assert.True(remoteProfiles.Count >= 2);

    vm.SelectedRemoteProfile = remoteProfiles[0];
    vm.EnterManualModeCommand.Execute(null);
    Assert.Equal(RemoteEditorMode.Manual, remoteProfiles[0].LastRemoteEditorMode);

    vm.SelectedRemoteProfile = remoteProfiles[1];
    Assert.False(vm.IsManualMode);
    Assert.Equal(RemoteEditorMode.Chooser, remoteProfiles[1].LastRemoteEditorMode);

    vm.SelectedRemoteProfile = remoteProfiles[0];
    Assert.True(vm.IsManualMode);
  }

  [Fact]
  public void EnterManualMode_PersistsModeOnSelectedProfile()
  {
    MainWindowViewModel vm = CreateViewModel();
    vm.AddRemoteCommand.Execute(null);
    vm.ShowRemoteEditor = true;

    MountProfile profile = vm.RemoteProfiles[0];
    vm.SelectedRemoteProfile = profile;

    vm.EnterManualModeCommand.Execute(null);
    Assert.Equal(RemoteEditorMode.Manual, profile.LastRemoteEditorMode);

    vm.ExitManualModeCommand.Execute(null);
    Assert.Equal(RemoteEditorMode.Chooser, profile.LastRemoteEditorMode);
  }

  [Fact]
  public void WizardUnavailableReason_EmptyWhenAllRequirementsMet()
  {
    MainWindowViewModel vm = CreateViewModel();
    vm.AddRemoteCommand.Execute(null);
    vm.ShowRemoteEditor = true;
    vm.SelectedRemoteProfile = vm.RemoteProfiles[0];
    vm.NewRemoteName = "test";
    vm.SelectedBackend = new RcloneBackendInfo {Name = "s3", Description = "Amazon S3"};

    Assert.Empty(vm.WizardUnavailableReason);
    Assert.False(vm.IsWizardUnavailable);
  }

  [Fact]
  public void WizardUnavailableReason_ReportsMissingBackend()
  {
    MainWindowViewModel vm = CreateViewModel();
    vm.AddRemoteCommand.Execute(null);
    vm.ShowRemoteEditor = true;
    vm.SelectedRemoteProfile = vm.RemoteProfiles[0];
    vm.NewRemoteName = "test";
    vm.SelectedBackend = null;

    Assert.True(vm.IsWizardUnavailable);
    Assert.Contains("backend", vm.WizardUnavailableReason, System.StringComparison.OrdinalIgnoreCase);
  }
}
