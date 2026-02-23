using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Linq;
using Avalonia.Markup.Xaml;
using RcloneMountManager.ViewModels;
using RcloneMountManager.Views;

namespace RcloneMountManager;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            var viewModel = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            desktop.Exit += (_, _) => viewModel.Dispose();
            viewModel.InitializeRuntimeMonitoring();
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

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
