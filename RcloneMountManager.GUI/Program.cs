using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RcloneMountManager.Core.Services;
using RcloneMountManager.Services;
using RcloneMountManager.ViewModels;
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

    var host = Host.CreateDefaultBuilder(args)
      .UseSerilog()
      .ConfigureServices(services =>
      {
        services.AddSingleton<MountManagerService>();
        services.AddSingleton<LaunchAgentService>();
        services.AddSingleton<RcloneBackendService>();
        services.AddSingleton<StartupPreflightService>();
        services.AddSingleton<MountHealthService>();
        services.AddSingleton<MainWindowViewModel>(sp =>
          new MainWindowViewModel(
            logger: sp.GetRequiredService<ILogger<MainWindowViewModel>>(),
            mountManagerService: sp.GetRequiredService<MountManagerService>(),
            launchAgentService: sp.GetRequiredService<LaunchAgentService>(),
            rcloneBackendService: sp.GetRequiredService<RcloneBackendService>(),
            startupPreflightService: sp.GetRequiredService<StartupPreflightService>(),
            mountHealthService: sp.GetRequiredService<MountHealthService>()));
      })
      .Build();

    App.Services = host.Services;

    try
    {
      Log.Information("Starting Rclone Mount Manager");
      host.Start();

      Console.CancelKeyPress += (_, e) =>
      {
        e.Cancel = true;
        Log.Information("Ctrl+C received, shutting down");
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
          if (Avalonia.Application.Current?.ApplicationLifetime
              is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
          {
            desktop.Shutdown();
          }
        });
      };

      BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    catch (Exception ex)
    {
      Log.Fatal(ex, "Application terminated unexpectedly");
      throw;
    }
    finally
    {
      host.StopAsync().GetAwaiter().GetResult();
      host.Dispose();
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
      .WriteTo.Sink(DiagnosticsSink.Instance)
      .CreateLogger();
  }
}
