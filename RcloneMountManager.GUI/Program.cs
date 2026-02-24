using Avalonia;
using Serilog;
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace RcloneMountManager;

sealed class Program
{
  private const string MutexName = "RcloneMountManager_SingleInstance";
  private const string PipeName = "RcloneMountManager_Activate";

  [STAThread]
  public static void Main(string[] args)
  {
    ConfigureLogging();

    using var mutex = new Mutex(true, MutexName, out bool createdNew);
    if (!createdNew)
    {
      Log.Information("Another instance is already running. Signalling it to activate.");
      SignalExistingInstance();
      return;
    }

    try
    {
      Log.Information("Starting Rclone Mount Manager");
      BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    catch (Exception ex)
    {
      Log.Fatal(ex, "Application terminated unexpectedly");
      throw;
    }
    finally
    {
      Log.CloseAndFlush();
    }
  }

  public static AppBuilder BuildAvaloniaApp()
    => AppBuilder.Configure<App>()
      .UsePlatformDetect()
      .WithInterFont()
      .LogToTrace();

  private static void SignalExistingInstance()
  {
    try
    {
      using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
      client.Connect(timeout: 2000);
      using var writer = new StreamWriter(client);
      writer.Write("ACTIVATE");
      writer.Flush();
    }
    catch (Exception ex)
    {
      Log.Warning(ex, "Could not signal existing instance");
    }
  }

  private static void ConfigureLogging()
  {
    var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
    Directory.CreateDirectory(logDirectory);

    var logPath = Path.Combine(logDirectory, "rclone-mount-.log");

    Log.Logger = new LoggerConfiguration()
      .MinimumLevel.Debug()
      .Enrich.FromLogContext()
      .WriteTo.Console()
      .WriteTo.File(
        path: logPath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
      .CreateLogger();
  }
}
