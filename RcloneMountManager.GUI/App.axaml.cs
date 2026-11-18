using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;
using RcloneMountManager.Views;
using Serilog;
using MainWindowViewModel = RcloneMountManager.GUI.ViewModels.MainWindowViewModel;

namespace RcloneMountManager;

public partial class App : Application
{
  public static IServiceProvider Services { get; set; } = null!;

  private const string PipeName = "RcloneMountManager_Activate";
  private CancellationTokenSource? _pipeCts;

  public override void Initialize()
  {
    AvaloniaXamlLoader.Load(this);
  }

  public override void OnFrameworkInitializationCompleted()
  {
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
      MainWindowViewModel viewModel = Services.GetRequiredService<MainWindowViewModel>();
      MainWindow mainWindow = new()
      {
        DataContext = viewModel,
      };
      desktop.MainWindow = mainWindow;

      desktop.Exit += (_, _) =>
      {
        _pipeCts?.Cancel();
        viewModel.Dispose();
      };

      viewModel.InitializeRuntimeMonitoring();
      StartActivationListener(mainWindow);
    }

    base.OnFrameworkInitializationCompleted();
  }

  public void EditableComboBox_GotFocus(object? sender, FocusChangedEventArgs e)
  {
    if (sender is ComboBox comboBox
        && !comboBox.IsDropDownOpen
        && e.NavigationMethod != NavigationMethod.Unspecified)
    {
      comboBox.IsDropDownOpen = true;
    }
  }

  private void StartActivationListener(Window mainWindow)
  {
    _pipeCts = new CancellationTokenSource();
    CancellationToken token = _pipeCts.Token;

    Task.Run(
      async () =>
      {
        while (!token.IsCancellationRequested)
        {
          try
          {
            using NamedPipeServerStream server = new(
              PipeName,
              PipeDirection.In,
              1,
              PipeTransmissionMode.Byte,
              PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync(token);
            using StreamReader reader = new(server);
            string message = await reader.ReadToEndAsync(token);

            if (message.Trim() == "ACTIVATE")
            {
              Log.Information("Received activation signal from another instance");
              Dispatcher.UIThread.Post(() =>
              {
                mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
                mainWindow.Topmost = true;
                mainWindow.Topmost = false;
              });
            }
          }
          catch (OperationCanceledException)
          {
            break;
          }
          catch (Exception ex)
          {
            Log.Warning(ex, "Activation listener error");
          }
        }
      },
      token);
  }

  private async void AboutMenuItem_OnClick(object? sender, EventArgs e)
  {
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
        desktop.MainWindow != null)
    {
      AboutWindow aboutWindow = new();
      await aboutWindow.ShowDialog(desktop.MainWindow);
    }
  }

  private void QuitMenuItem_OnClick(object? sender, EventArgs e)
  {
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
      desktop.Shutdown();
    }
  }
}