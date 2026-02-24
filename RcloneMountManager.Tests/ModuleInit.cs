using System.Runtime.CompilerServices;
using RcloneMountManager.Services;
using Serilog;
using Serilog.Events;

namespace RcloneMountManager.Tests;

internal static class ModuleInit
{
  [ModuleInitializer]
  internal static void Initialize()
  {
    Log.Logger = new LoggerConfiguration()
      .MinimumLevel.Debug()
      .Enrich.FromLogContext()
      .WriteTo.Sink(DiagnosticsSink.Instance)
      .CreateLogger();
  }
}
