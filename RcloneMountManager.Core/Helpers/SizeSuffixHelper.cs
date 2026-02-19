using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RcloneMountManager.Core.Helpers;

public static partial class SizeSuffixHelper
{
    public static IReadOnlyList<string> Units { get; } = ["B", "Ki", "Mi", "Gi", "Ti"];

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)\s*(Ki|Mi|Gi|Ti|B)?$", RegexOptions.Compiled)]
    private static partial Regex SizeRegex();

    public static (decimal Value, string Unit) Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || string.Equals(input, "off", StringComparison.OrdinalIgnoreCase))
            return (0m, "B");

        var match = SizeRegex().Match(input.Trim());
        if (!match.Success)
            return (0m, "B");

        var value = decimal.Parse(match.Groups[1].Value);
        var unit = match.Groups[2].Success && !string.IsNullOrEmpty(match.Groups[2].Value)
            ? match.Groups[2].Value
            : "B";

        return (value, unit);
    }

    public static string Format(decimal value, string unit)
    {
        if (value == 0m && unit == "B") return "0";

        var intValue = (long)value;
        return unit == "B" ? $"{intValue}" : $"{intValue}{unit}";
    }
}
