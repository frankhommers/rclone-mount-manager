using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RcloneMountManager.Core.Services;
using RcloneMountManager.GUI.Services;
using Serilog;
using MainWindowViewModel = RcloneMountManager.GUI.ViewModels.MainWindowViewModel;

namespace RcloneMountManager.GUI;

internal sealed class Program
{
  private const string MutexName = "RcloneMountManager_SingleInstance";
  private const string PipeName = "RcloneMountManager_Activate";

  [STAThread]
  public static void Main(string[] args)
  {
    ConfigureLogging();

    using Mutex mutex = new(true, MutexName, out bool createdNew);
    if (!createdNew)
    {
      Log.Information("Another instance is already running. Signalling it to activate.");
      SignalExistingInstance();
      return;
    }

    IHost host = Host.CreateDefaultBuilder(args)
      .UseSerilog()
      .ConfigureServices(services =>
      {
        services.AddSingleton<MountManagerService>();
        services.AddSingleton<LaunchAgentService>();
        services.AddSingleton<RcloneBackendService>();
        services.AddSingleton<RcloneConfigWizardService>();
        services.AddSingleton<StartupPreflightService>();
        services.AddSingleton<MountHealthService>();
        services.AddSingleton<MainWindowViewModel>(sp =>
                                                     new MainWindowViewModel(
                                                       logger: sp.GetRequiredService<ILogger<MainWindowViewModel>>(),
                                                       mountManagerService:
                                                       sp.GetRequiredService<MountManagerService>(),
                                                       launchAgentService: sp.GetRequiredService<LaunchAgentService>(),
                                                       rcloneBackendService:
                                                       sp.GetRequiredService<RcloneBackendService>(),
                                                       rcloneConfigWizardService: sp
                                                         .GetRequiredService<RcloneConfigWizardService>(),
                                                       startupPreflightService: sp
                                                         .GetRequiredService<StartupPreflightService>(),
                                                       mountHealthService: sp
                                                         .GetRequiredService<MountHealthService>()));
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
          if (Application.Current?.ApplicationLifetime
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
  {
    return AppBuilder.Configure<App>()
      .UsePlatformDetect()
      .WithInterFont()
      .LogToTrace();
  }

  private static void SignalExistingInstance()
  {
    try
    {
      using NamedPipeClientStream client = new(".", PipeName, PipeDirection.Out);
      client.Connect(2000);
      using StreamWriter writer = new(client);
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
    string logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
    Directory.CreateDirectory(logDirectory);

    string logPath = Path.Combine(logDirectory, "rclone-mount-.log");

    Log.Logger = new LoggerConfiguration()
      .MinimumLevel.Debug()
      .Enrich.FromLogContext()
      .WriteTo.Console()
      .WriteTo.File(
        logPath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
      .WriteTo.Sink(DiagnosticsSink.Instance)
      .CreateLogger();
  }
}