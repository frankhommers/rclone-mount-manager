using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Input;
using Avalonia.Threading;
using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;
using RcloneMountManager.ViewModels;
using RcloneMountManager.Views;
using Serilog;

namespace RcloneMountManager;

public partial class App : Application
{
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
      DisableAvaloniaDataAnnotationValidation();
      var viewModel = new MainWindowViewModel();
      var mainWindow = new MainWindow
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

  public void EditableComboBox_GotFocus(object? sender, GotFocusEventArgs e)
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
    var token = _pipeCts.Token;

    Task.Run(async () =>
    {
      while (!token.IsCancellationRequested)
      {
        try
        {
          using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
          await server.WaitForConnectionAsync(token);
          using var reader = new StreamReader(server);
          var message = await reader.ReadToEndAsync(token);

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
    }, token);
  }

  private void DisableAvaloniaDataAnnotationValidation()
  {
    var dataValidationPluginsToRemove =
      BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

    foreach (var plugin in dataValidationPluginsToRemove)
    {
      BindingPlugins.DataValidators.Remove(plugin);
    }
  }
}
