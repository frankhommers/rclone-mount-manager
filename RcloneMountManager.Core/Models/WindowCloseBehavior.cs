using System.ComponentModel;

namespace RcloneMountManager.Core.Models;

public enum WindowCloseBehavior
{
  [Description("Minimize to menubar")]
  MinimizeToMenubar,

  [Description("Minimize to dock")]
  MinimizeToDock,

  [Description("Quit application")]
  Quit,
}
