using System.Collections.Generic;

namespace RcloneMountManager.Core.Models;

public sealed class ConfigWizardStep
{
  public string State { get; init; } = string.Empty;
  public string Name { get; init; } = string.Empty;
  public string Help { get; init; } = string.Empty;
  public string Type { get; init; } = "string";
  public string DefaultValue { get; init; } = string.Empty;
  public bool Required { get; init; }
  public bool IsPassword { get; init; }
  public bool Exclusive { get; init; }
  public string Error { get; init; } = string.Empty;
  public List<ConfigWizardExample> Examples { get; init; } = [];

  public bool IsComplete => string.IsNullOrEmpty(State);
  public bool IsOAuthBrowserPrompt => string.Equals(Name, "config_is_local", System.StringComparison.Ordinal);
  public bool IsAdvancedPrompt => string.Equals(Name, "config_fs_advanced", System.StringComparison.Ordinal);
}
