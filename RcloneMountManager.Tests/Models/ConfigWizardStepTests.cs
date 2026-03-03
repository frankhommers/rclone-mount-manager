using RcloneMountManager.Core.Models;

namespace RcloneMountManager.Tests.Models;

public class ConfigWizardStepTests
{
  [Fact]
  public void IsComplete_WhenStateEmpty_ReturnsTrue()
  {
    var step = new ConfigWizardStep { State = "" };
    Assert.True(step.IsComplete);
  }

  [Fact]
  public void IsComplete_WhenStateHasValue_ReturnsFalse()
  {
    var step = new ConfigWizardStep { State = "*oauth-islocal,choose_type,," };
    Assert.False(step.IsComplete);
  }

  [Fact]
  public void IsOAuthBrowserPrompt_WhenNameIsConfigIsLocal_ReturnsTrue()
  {
    var step = new ConfigWizardStep { State = "x", Name = "config_is_local" };
    Assert.True(step.IsOAuthBrowserPrompt);
  }

  [Fact]
  public void IsOAuthBrowserPrompt_WhenNameIsDifferent_ReturnsFalse()
  {
    var step = new ConfigWizardStep { State = "x", Name = "client_id" };
    Assert.False(step.IsOAuthBrowserPrompt);
  }

  [Fact]
  public void IsAdvancedPrompt_WhenNameIsConfigFsAdvanced_ReturnsTrue()
  {
    var step = new ConfigWizardStep { State = "x", Name = "config_fs_advanced" };
    Assert.True(step.IsAdvancedPrompt);
  }

  [Fact]
  public void IsAdvancedPrompt_WhenNameIsDifferent_ReturnsFalse()
  {
    var step = new ConfigWizardStep { State = "x", Name = "region" };
    Assert.False(step.IsAdvancedPrompt);
  }

  [Fact]
  public void DefaultValues_AreCorrect()
  {
    var step = new ConfigWizardStep();
    Assert.Empty(step.State);
    Assert.Empty(step.Name);
    Assert.Empty(step.Help);
    Assert.Equal("string", step.Type);
    Assert.Empty(step.DefaultValue);
    Assert.False(step.Required);
    Assert.False(step.IsPassword);
    Assert.False(step.Exclusive);
    Assert.Empty(step.Error);
    Assert.Empty(step.Examples);
    Assert.True(step.IsComplete);
  }

  [Fact]
  public void Examples_CanBePopulated()
  {
    var step = new ConfigWizardStep
    {
      State = "x",
      Name = "region",
      Examples =
      [
        new ConfigWizardExample { Value = "global", Help = "Microsoft Cloud Global" },
        new ConfigWizardExample { Value = "us", Help = "US Government" },
      ],
    };

    Assert.Equal(2, step.Examples.Count);
    Assert.Equal("global", step.Examples[0].Value);
    Assert.Equal("US Government", step.Examples[1].Help);
  }
}
