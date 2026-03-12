using Avalonia.Controls;
using RcloneMountManager.GUI.Controls;
using RcloneMountManager.Core.Models;
using RcloneMountManager.GUI.ViewModels;

namespace RcloneMountManager.Views;

public partial class MainWindow : Window
{
  public MainWindow()
  {
    InitializeComponent();
    Resources["BackendOptionTemplateSelector"] = OptionTemplateSelectorFactory.Create(this, ActualThemeVariant);
  }

  private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
  {
    if (e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
    {
      return;
    }

    if (DataContext is not MainWindowViewModel viewModel)
    {
      return;
    }

    switch (viewModel.SelectedWindowCloseBehavior)
    {
      case WindowCloseBehavior.Quit:
        return;
      case WindowCloseBehavior.MinimizeToDock:
        e.Cancel = true;
        WindowState = WindowState.Minimized;
        return;
      case WindowCloseBehavior.MinimizeToMenubar:
      default:
        e.Cancel = true;
        Hide();
        return;
    }
  }
}
