using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Styling;
using Serilog;

namespace RcloneMountManager.GUI.Controls;

public static class OptionTemplateSelectorFactory
{
  private static readonly string[] Keys =
    ["Toggle", "ComboBox", "EditableComboBox", "Numeric", "Duration", "SizeSuffix", "StringList", "Text"];

  public static OptionControlTemplateSelector Create(Control host, ThemeVariant themeVariant)
  {
    OptionControlTemplateSelector selector = new();
    foreach (string key in Keys)
    {
      // Try the host's own tree first, then fall back to Application resources.
      // UserControl constructors run before the control is in the visual tree,
      // so host.TryFindResource won't reach Application-level resources.
      if (host.TryFindResource(key, themeVariant, out object? resource) && resource is IDataTemplate template)
      {
        selector.Templates[key] = template;
      }
      else if (Application.Current?.TryFindResource(key, themeVariant, out object? appResource) == true
               && appResource is IDataTemplate appTemplate)
      {
        selector.Templates[key] = appTemplate;
      }
      else
      {
        Log.Warning("Template '{Key}' not found in resources", key);
      }
    }

    return selector;
  }
}