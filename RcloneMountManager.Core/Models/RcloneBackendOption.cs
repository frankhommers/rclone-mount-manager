using System;
using System.Collections.Generic;

namespace RcloneMountManager.Core.Models;

public sealed class RcloneBackendOption : IRcloneOptionDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Help { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public string DefaultStr { get; set; } = string.Empty;
    public bool Required { get; set; }
    public bool IsPassword { get; set; }
    public bool Advanced { get; set; }
    public IReadOnlyList<string>? Examples { get; set; }
    public string ListSeparator => Type == "SpaceSepList" ? " " : ",";
    public bool IsKeyValue => string.Equals(Name, "headers", StringComparison.OrdinalIgnoreCase);

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
        if (GetEnumValues() is not null) return OptionControlType.ComboBox;
        return OptionControlType.Text;
    }

    public IReadOnlyList<string>? GetEnumValues()
    {
        if (Type == "bool") return null;
        return Examples is { Count: > 0 } ? Examples : null;
    }
}
