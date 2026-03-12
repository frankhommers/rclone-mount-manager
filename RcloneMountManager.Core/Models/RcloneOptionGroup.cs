using System.Collections.Generic;

namespace RcloneMountManager.Core.Models;

public sealed class RcloneOptionGroup
{
  public string Name { get; set; } = string.Empty;
  public string DisplayName { get; set; } = string.Empty;
  public IReadOnlyList<RcloneOption> Options { get; set; } = [];
}