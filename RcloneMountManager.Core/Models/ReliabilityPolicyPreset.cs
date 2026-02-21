using System;
using System.Collections.Generic;

namespace RcloneMountManager.Core.Models;

public sealed record ReliabilityPolicyPreset(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyDictionary<string, string> OptionOverrides)
{
    public const string ConservativeId = "conservative";
    public const string BalancedId = "balanced";
    public const string AggressiveId = "aggressive";

    public static IReadOnlySet<string> ManagedReliabilityKeys { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "vfs_cache_mode",
        "dir_cache_time",
        "attr_timeout",
        "retries",
        "low_level_retries",
        "retries_sleep",
    };

    public static IReadOnlyList<ReliabilityPolicyPreset> Catalog { get; } =
    [
        new(
            ConservativeId,
            "Conservative",
            "Prioritizes safer cache behavior and stronger retry tolerance.",
            CreateOverrides(
                ("vfs_cache_mode", "writes"),
                ("dir_cache_time", "2m"),
                ("attr_timeout", "2s"),
                ("retries", "5"),
                ("low_level_retries", "20"),
                ("retries_sleep", "10s"))),
        new(
            BalancedId,
            "Balanced",
            "Balances responsiveness and resilience for day-to-day usage.",
            CreateOverrides(
                ("vfs_cache_mode", "writes"),
                ("dir_cache_time", "5m"),
                ("attr_timeout", "1s"),
                ("retries", "3"),
                ("low_level_retries", "10"),
                ("retries_sleep", "5s"))),
        new(
            AggressiveId,
            "Aggressive",
            "Optimizes for fewer metadata refreshes and heavier retry persistence.",
            CreateOverrides(
                ("vfs_cache_mode", "full"),
                ("dir_cache_time", "15m"),
                ("attr_timeout", "5s"),
                ("retries", "10"),
                ("low_level_retries", "30"),
                ("retries_sleep", "3s"))),
    ];

    public static ReliabilityPolicyPreset GetByIdOrDefault(string? presetId)
    {
        if (!string.IsNullOrWhiteSpace(presetId) && CatalogById.TryGetValue(presetId, out var preset))
        {
            return preset;
        }

        return CatalogById[BalancedId];
    }

    private static IReadOnlyDictionary<string, ReliabilityPolicyPreset> CatalogById { get; } = CreateCatalogById();

    private static IReadOnlyDictionary<string, string> CreateOverrides(params (string key, string value)[] values)
    {
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
        {
            overrides[key] = value;
        }

        return overrides;
    }

    private static IReadOnlyDictionary<string, ReliabilityPolicyPreset> CreateCatalogById()
    {
        var byId = new Dictionary<string, ReliabilityPolicyPreset>(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in Catalog)
        {
            byId[preset.Id] = preset;
        }

        return byId;
    }
}
