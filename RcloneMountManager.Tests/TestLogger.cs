using Microsoft.Extensions.Logging;
using RcloneMountManager.ViewModels;
using Serilog.Extensions.Logging;

namespace RcloneMountManager.Tests;

internal static class TestLogger
{
  private static readonly SerilogLoggerFactory Factory = new(Serilog.Log.Logger, dispose: false);

  public static ILogger<MainWindowViewModel> CreateMainWindowViewModelLogger()
  {
    return Factory.CreateLogger<MainWindowViewModel>();
  }
}
