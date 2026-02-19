using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Styling;

namespace RcloneMountManager.Controls;

public static class OptionTemplateSelectorFactory
{
    private static readonly string[] Keys = ["Toggle", "ComboBox", "Numeric", "Duration", "SizeSuffix", "Text"];

    public static OptionControlTemplateSelector Create(Control host, ThemeVariant themeVariant)
    {
        var selector = new OptionControlTemplateSelector();
        foreach (var key in Keys)
        {
            if (host.TryFindResource(key, themeVariant, out var resource) && resource is IDataTemplate)
            {
                var template = (IDataTemplate)resource;
                selector.Templates[key] = template;
            }
        }

        return selector;
    }
}
