using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;
using RcloneMountManager.Core.ViewModels;

namespace RcloneMountManager.GUI.Controls;

public class OptionControlTemplateSelector : IDataTemplate
{
  [Content] public Dictionary<string, IDataTemplate> Templates { get; } = new();

  public Control? Build(object? param)
  {
    if (param is not TypedOptionViewModel vm)
    {
      return null;
    }

    string key = vm.ControlType switch
    {
      Core.Models.OptionControlType.Toggle => "Toggle",
      Core.Models.OptionControlType.ComboBox => "ComboBox",
      Core.Models.OptionControlType.EditableComboBox => "EditableComboBox",
      Core.Models.OptionControlType.Numeric => "Numeric",
      Core.Models.OptionControlType.Duration => "Duration",
      Core.Models.OptionControlType.SizeSuffix => "SizeSuffix",
      Core.Models.OptionControlType.StringList => "StringList",
      _ => "Text",
    };

    if (Templates.TryGetValue(key, out IDataTemplate? template))
    {
      return template.Build(param);
    }

    // Fallback: if specific template not found, try Text
    if (Templates.TryGetValue("Text", out IDataTemplate? fallback))
    {
      return fallback.Build(param);
    }

    return null;
  }

  public bool Match(object? data)
  {
    return data is TypedOptionViewModel;
  }
}