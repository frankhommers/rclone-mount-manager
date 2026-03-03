using System;
using System.Collections.Generic;
using System.Linq;

namespace RcloneMountManager.Core.Models;

public sealed class WizardStepOptionDefinition : IRcloneOptionDefinition
{
  private readonly ConfigWizardStep _step;
  private readonly IReadOnlyList<string>? _enumValues;

  public WizardStepOptionDefinition(ConfigWizardStep step)
  {
    _step = step;
    _enumValues = step.Examples is { Count: > 0 }
      ? step.Examples.Select(e => e.Value).Where(v => !string.IsNullOrEmpty(v)).ToList()
      : null;
  }

  public string Name => _step.Name;
  public string Help => _step.Help;
  public string Type => _step.Type;
  public string DefaultStr => _step.DefaultValue;
  public bool Advanced => false;
  public bool Required => _step.Required;
  public bool IsPassword => _step.IsPassword;
  public string ListSeparator => ",";
  public bool IsKeyValue => false;

  public OptionControlType GetControlType()
  {
    if (IsPassword) return OptionControlType.Text;

    var typeControl = Type switch
    {
      "bool" => OptionControlType.Toggle,
      "int" or "int64" or "uint32" or "float64" => OptionControlType.Numeric,
      "Duration" => OptionControlType.Duration,
      "SizeSuffix" => OptionControlType.SizeSuffix,
      "CommaSepList" or "SpaceSepList" or "stringArray" => OptionControlType.StringList,
      _ => OptionControlType.Text,
    };

    if (typeControl is not OptionControlType.Text) return typeControl;

    if (_enumValues is not null)
    {
      return _step.Exclusive ? OptionControlType.ComboBox : OptionControlType.EditableComboBox;
    }

    return OptionControlType.Text;
  }

  public IReadOnlyList<string>? GetEnumValues()
  {
    if (Type == "bool") return null;
    return _enumValues;
  }
}
