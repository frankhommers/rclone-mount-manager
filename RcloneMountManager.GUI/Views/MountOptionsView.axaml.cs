using Avalonia.Controls;
using Avalonia.Controls.Templates;
using RcloneMountManager.Controls;

namespace RcloneMountManager.Views;

public partial class MountOptionsView : UserControl
{
    public MountOptionsView()
    {
        InitializeComponent();

        var selector = new OptionControlTemplateSelector();
        var keys = new[] { "Toggle", "ComboBox", "Numeric", "Duration", "SizeSuffix", "Text" };
        foreach (var key in keys)
        {
            if (this.TryFindResource(key, this.ActualThemeVariant, out var resource) && resource is IDataTemplate template)
            {
                selector.Templates[key] = template;
            }
        }

        Resources["OptionTemplateSelector"] = selector;
    }
}
