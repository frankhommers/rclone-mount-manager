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
            // Try the host's own tree first, then fall back to Application resources.
            // UserControl constructors run before the control is in the visual tree,
            // so host.TryFindResource won't reach Application-level resources.
            if (host.TryFindResource(key, themeVariant, out var resource) && resource is IDataTemplate template)
            {
                selector.Templates[key] = template;
            }
            else if (Application.Current?.TryFindResource(key, themeVariant, out var appResource) == true
                     && appResource is IDataTemplate appTemplate)
            {
                selector.Templates[key] = appTemplate;
            }
        }

        return selector;
    }
}
