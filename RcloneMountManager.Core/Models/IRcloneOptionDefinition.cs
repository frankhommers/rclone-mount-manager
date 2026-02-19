using System.Collections.Generic;

namespace RcloneMountManager.Core.Models;

public interface IRcloneOptionDefinition
{
    string Name { get; }
    string Help { get; }
    string Type { get; }
    string DefaultStr { get; }
    bool Advanced { get; }
    bool Required { get; }
    bool IsPassword { get; }
    OptionControlType GetControlType();
    IReadOnlyList<string>? GetEnumValues();
}
