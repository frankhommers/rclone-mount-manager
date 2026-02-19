using System;
using System.Text.RegularExpressions;

namespace RcloneMountManager.Core.Helpers;

public static partial class DurationHelper
{
    [GeneratedRegex(@"(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?", RegexOptions.Compiled)]
    private static partial Regex DurationRegex();

    public static TimeSpan Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input == "0")
            return TimeSpan.Zero;

        // Try pure seconds first (e.g., "90s" or just "90")
        if (long.TryParse(input.TrimEnd('s'), out var totalSeconds) && !input.Contains('h') && !input.Contains('m'))
            return TimeSpan.FromSeconds(totalSeconds);

        var match = DurationRegex().Match(input);
        if (!match.Success || match.Length == 0)
            return TimeSpan.Zero;

        int hours = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
        int minutes = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
        int seconds = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;

        return new TimeSpan(hours, minutes, seconds);
    }

    public static string Format(TimeSpan ts)
    {
        if (ts <= TimeSpan.Zero) return "0s";

        var parts = new System.Text.StringBuilder();
        if (ts.Hours > 0) parts.Append($"{ts.Hours}h");
        if (ts.Minutes > 0) parts.Append($"{ts.Minutes}m");
        if (ts.Seconds > 0 || parts.Length == 0) parts.Append($"{ts.Seconds}s");

        return parts.ToString();
    }
}
