namespace RcloneMountManager.Core.Models;

public partial class WizardStepOptionInput : ViewModels.TypedOptionViewModel
{
  private readonly WizardStepOptionDefinition _option;

  public WizardStepOptionInput(ConfigWizardStep step)
  {
    _option = new WizardStepOptionDefinition(step);
    InitializeTypedValues(step.DefaultValue);
  }

  protected override IRcloneOptionDefinition Option => _option;

  public override string Label => _option.Help;
}