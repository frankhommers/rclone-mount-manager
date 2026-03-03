using RcloneMountManager.ViewModels;

namespace RcloneMountManager.Tests.ViewModels;

public class MainWindowViewModelWizardTests
{
  [Fact]
  public void IsWizardActive_DefaultsFalse()
  {
    var vm = new MainWindowViewModel(loadStartupData: false);
    Assert.False(vm.IsWizardActive);
  }

  [Fact]
  public void ShowWizardContent_FalseWhenWizardInactive()
  {
    var vm = new MainWindowViewModel(loadStartupData: false);
    Assert.False(vm.ShowWizardContent);
  }

  [Fact]
  public void ShowStandardRemoteForm_TrueWhenRemoteEditorActiveAndWizardInactive()
  {
    var vm = new MainWindowViewModel(loadStartupData: false);
    vm.ShowRemoteEditor = true;
    Assert.True(vm.ShowStandardRemoteForm);
  }

  [Fact]
  public void ShowStandardRemoteForm_FalseWhenWizardActive()
  {
    var vm = new MainWindowViewModel(loadStartupData: false);
    vm.ShowRemoteEditor = true;
    vm.IsWizardActive = true;
    Assert.False(vm.ShowStandardRemoteForm);
  }

  [Fact]
  public void WizardStepTitle_EmptyWhenNoStep()
  {
    var vm = new MainWindowViewModel(loadStartupData: false);
    Assert.Empty(vm.WizardStepTitle);
  }

  [Fact]
  public void WizardStepHelp_EmptyWhenNoStep()
  {
    var vm = new MainWindowViewModel(loadStartupData: false);
    Assert.Empty(vm.WizardStepHelp);
  }

  [Fact]
  public void WizardHasExamples_FalseWhenNoStep()
  {
    var vm = new MainWindowViewModel(loadStartupData: false);
    Assert.False(vm.WizardHasExamples);
  }

  [Fact]
  public async Task CancelWizard_ResetsState()
  {
    var vm = new MainWindowViewModel(loadStartupData: false);
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
}
