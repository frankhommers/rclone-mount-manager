using System.Collections.Generic;

namespace RcloneMountManager.Core.Models;

public sealed class RcloneOption : IRcloneOptionDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Help { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public object? Default { get; set; }
    public string DefaultStr { get; set; } = string.Empty;
    public bool Advanced { get; set; }
    public bool Required { get; set; }
    public bool IsPassword { get; set; }
    public string? Groups { get; set; }
    public string ListSeparator => Type == "SpaceSepList" ? " " : ",";
    public bool IsKeyValue => string.Equals(Name, "headers", System.StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string>? GetEnumValues()
    {
        return Type switch
        {
            "CacheMode" => ["off", "minimal", "writes", "full"],
            "LogLevel" => ["DEBUG", "INFO", "NOTICE", "ERROR"],
            "Tristate" => ["unset", "true", "false"],
            _ when Type.Contains('|') => Type.Split('|'),
            _ => null,
        };
    }

    public OptionControlType GetControlType()
    {
        if (GetEnumValues() is not null) return OptionControlType.ComboBox;

        return Type switch
        {
            "bool" => OptionControlType.Toggle,
            "int" or "int64" or "uint32" or "float64" => OptionControlType.Numeric,
            "Duration" => OptionControlType.Duration,
            "SizeSuffix" => OptionControlType.SizeSuffix,
            "FileMode" => OptionControlType.Text,
            "string" => OptionControlType.Text,
            "stringArray" or "SpaceSepList" or "CommaSepList" => OptionControlType.StringList,
            "BwTimetable" or "DumpFlags" or "Bits" or "Time" => OptionControlType.Text,
            _ => OptionControlType.Text,
        };
    }
}

public enum OptionControlType
{
    Text,
    Toggle,
    Numeric,
    Duration,
    SizeSuffix,
    StringList,
    ComboBox,
}
