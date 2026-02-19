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

    public OptionControlType GetControlType()
    {
        if (IsPassword) return OptionControlType.Text;
        if (GetEnumValues() is not null) return OptionControlType.ComboBox;

        return Type switch
        {
            "bool" => OptionControlType.Toggle,
            "int" or "int64" or "uint32" or "float64" => OptionControlType.Numeric,
            "Duration" => OptionControlType.Duration,
            "SizeSuffix" => OptionControlType.SizeSuffix,
            _ => OptionControlType.Text,
        };
    }

    public IReadOnlyList<string>? GetEnumValues() => Examples is { Count: > 0 } ? Examples : null;
}
