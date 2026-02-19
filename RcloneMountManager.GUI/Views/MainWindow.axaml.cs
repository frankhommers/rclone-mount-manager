using Avalonia.Controls;
using RcloneMountManager.Controls;

namespace RcloneMountManager.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Resources["BackendOptionTemplateSelector"] = OptionTemplateSelectorFactory.Create(this, ActualThemeVariant);
    }
}
