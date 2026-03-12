using System.Collections.ObjectModel;
using RcloneMountManager.Core.Models;
using RcloneMountManager.GUI.ViewModels;

namespace RcloneMountManager.Tests.ViewModels;

public class MainWindowViewModelWizardTests
{
  [Fact]
  public void IsWizardActive_DefaultsFalse()
  {
    MainWindowViewModel vm = new(loadStartupData: false);
    Assert.False(vm.IsWizardActive);
  }

  [Fact]
  public void ShowWizardContent_FalseWhenWizardInactive()
  {
    MainWindowViewModel vm = new(loadStartupData: false);
    Assert.False(vm.ShowWizardContent);
  }

  [Fact]
  public void ShowStandardRemoteForm_TrueWhenRemoteEditorActiveAndWizardInactive()
  {
    MainWindowViewModel vm = new(loadStartupData: false);
    vm.ShowRemoteEditor = true;
    Assert.True(vm.ShowStandardRemoteForm);
  }

  [Fact]
  public void ShowStandardRemoteForm_FalseWhenWizardActive()
  {
    MainWindowViewModel vm = new(loadStartupData: false);
    vm.ShowRemoteEditor = true;
    vm.IsWizardActive = true;
    Assert.False(vm.ShowStandardRemoteForm);
  }

  [Fact]
  public void WizardStepTitle_EmptyWhenNoStep()
  {
    MainWindowViewModel vm = new(loadStartupData: false);
    Assert.Empty(vm.WizardStepTitle);
  }

  [Fact]
  public void WizardStepHelp_EmptyWhenNoStep()
  {
    MainWindowViewModel vm = new(loadStartupData: false);
    Assert.Empty(vm.WizardStepHelp);
  }

  [Fact]
  public void WizardStepHelp_ReplacesNewLinesWithSpaces()
  {
    MainWindowViewModel vm = new(loadStartupData: false);
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
    MainWindowViewModel vm = new(loadStartupData: false);
    Assert.False(vm.WizardHasExamples);
  }

  [Fact]
  public void WizardHasExamples_TrueWhenExclusiveAndExamplesExist()
  {
    MainWindowViewModel vm = new(loadStartupData: false);
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
    MainWindowViewModel vm = new(loadStartupData: false);
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
    MainWindowViewModel vm = new(loadStartupData: false);
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
    MainWindowViewModel vm = new(loadStartupData: false);
    vm.ShowRemoteEditor = true;
    Assert.True(vm.ShowRemoteChooser);
  }

  [Fact]
  public void ShowRemoteChooser_FalseWhenWizardActive()
  {
    MainWindowViewModel vm = new(loadStartupData: false);
    vm.ShowRemoteEditor = true;
    vm.IsWizardActive = true;
    Assert.False(vm.ShowRemoteChooser);
  }

  [Fact]
  public void ShowRemoteChooser_FalseWhenManualMode()
  {
    MainWindowViewModel vm = new(loadStartupData: false);
    vm.ShowRemoteEditor = true;
    vm.IsManualMode = true;
    Assert.False(vm.ShowRemoteChooser);
  }

  [Fact]
  public void ShowManualRemoteForm_TrueWhenManualModeAndRemoteEditorActive()
  {
    MainWindowViewModel vm = new(loadStartupData: false);
    vm.ShowRemoteEditor = true;
    vm.IsManualMode = true;
    Assert.True(vm.ShowManualRemoteForm);
  }

  [Fact]
  public void ShowManualRemoteForm_FalseWhenNotManualMode()
  {
    MainWindowViewModel vm = new(loadStartupData: false);
    vm.ShowRemoteEditor = true;
    Assert.False(vm.ShowManualRemoteForm);
  }

  [Fact]
  public void EnterManualMode_SetsIsManualModeTrue()
  {
    MainWindowViewModel vm = new(loadStartupData: false);
    vm.ShowRemoteEditor = true;
    vm.EnterManualModeCommand.Execute(null);
    Assert.True(vm.IsManualMode);
    Assert.True(vm.ShowManualRemoteForm);
    Assert.False(vm.ShowRemoteChooser);
  }

  [Fact]
  public void ExitManualMode_SetsIsManualModeFalse()
  {
    MainWindowViewModel vm = new(loadStartupData: false);
    vm.ShowRemoteEditor = true;
    vm.IsManualMode = true;
    vm.ExitManualModeCommand.Execute(null);
    Assert.False(vm.IsManualMode);
    Assert.True(vm.ShowRemoteChooser);
    Assert.False(vm.ShowManualRemoteForm);
  }

  [Fact]
  public void SwitchingRemoteProfile_ResetsManualMode()
  {
    MainWindowViewModel vm = new(loadStartupData: false);
    vm.AddRemoteCommand.Execute(null);
    vm.AddRemoteCommand.Execute(null);
    vm.ShowRemoteEditor = true;

    ObservableCollection<MountProfile> remoteProfiles = vm.RemoteProfiles;
    Assert.True(remoteProfiles.Count >= 2);

    vm.SelectedRemoteProfile = remoteProfiles[0];
    vm.IsManualMode = true;

    vm.SelectedRemoteProfile = remoteProfiles[1];
    Assert.False(vm.IsManualMode);
  }
}