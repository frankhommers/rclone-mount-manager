using CliWrap;
using CliWrap.Buffered;
using RcloneMountManager.Core.Models;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RcloneMountManager.Core.Services;

public sealed class RcloneOptionsService
{
    private static readonly (string Key, string Display)[] MountRelevantGroups =
    [
        ("mount", "Mount"),
        ("vfs", "VFS Cache"),
        ("nfs", "NFS"),
        ("filter", "Filters"),
        ("main", "General"),
        ("rc", "Remote Control"),
    ];

    private static readonly HashSet<string> RcBasicOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "rc", "rc_addr", "rc_user", "rc_pass", "rc_no_auth",
    };

    public async Task<IReadOnlyList<RcloneOptionGroup>> GetMountOptionsAsync(
        string rcloneBinaryPath,
        CancellationToken cancellationToken)
    {
        var binary = string.IsNullOrWhiteSpace(rcloneBinaryPath) ? "rclone" : rcloneBinaryPath;

        var result = await Cli.Wrap(binary)
            .WithArguments(["rc", "--loopback", "options/info"])
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(result.StandardError)
                    ? "Could not retrieve rclone options."
                    : result.StandardError.Trim());
        }

        return ParseOptionsJson(result.StandardOutput);
    }

    public static IReadOnlyList<RcloneOptionGroup> ParseOptionsJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var groups = new List<RcloneOptionGroup>();

        foreach (var (key, display) in MountRelevantGroups)
        {
            if (!root.TryGetProperty(key, out var groupElement))
                continue;

            var options = new List<RcloneOption>();
            foreach (var optElement in groupElement.EnumerateArray())
            {
                var option = new RcloneOption
                {
                    Name = optElement.GetProperty("Name").GetString() ?? string.Empty,
                    Help = optElement.GetProperty("Help").GetString() ?? string.Empty,
                    Type = optElement.GetProperty("Type").GetString() ?? "string",
                    DefaultStr = optElement.GetProperty("DefaultStr").GetString() ?? string.Empty,
                    Advanced = optElement.TryGetProperty("Advanced", out var adv) && adv.GetBoolean(),
                    Required = optElement.TryGetProperty("Required", out var req) && req.GetBoolean(),
                    IsPassword = optElement.TryGetProperty("IsPassword", out var pwd) && pwd.GetBoolean(),
                    Groups = optElement.TryGetProperty("Groups", out var grp) ? grp.GetString() : null,
                };

                if (!string.IsNullOrWhiteSpace(option.Name))
                    options.Add(option);
            }

            if (key == "rc")
            {
                options.RemoveAll(o => o.Name.StartsWith("metrics_", StringComparison.OrdinalIgnoreCase));
                foreach (var opt in options)
                {
                    if (!RcBasicOptions.Contains(opt.Name))
                        opt.Advanced = true;
                }
            }

            groups.Add(new RcloneOptionGroup
            {
                Name = key,
                DisplayName = display,
                Options = options,
            });
        }

        return groups;
    }
}
