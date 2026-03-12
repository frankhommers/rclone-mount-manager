using System.Collections.Generic;

namespace RcloneMountManager.Core.Models;

public sealed class RcloneBackendInfo
{
  public string Name { get; set; } = string.Empty;
  public string Description { get; set; } = string.Empty;
  public bool RequiresOAuth { get; set; }
  public List<RcloneBackendOption> Options { get; set; } = [];

  public string DisplayName => Name;

  public string Details => Description;

  public override string ToString()
  {
    return DisplayName;
  }
}