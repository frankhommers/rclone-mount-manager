using CliWrap;
using CliWrap.Buffered;
using RcloneMountManager.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RcloneMountManager.Services;

public sealed class RcloneBackendService
{
    public async Task<IReadOnlyList<RcloneBackendInfo>> GetBackendsAsync(string rcloneBinaryPath, CancellationToken cancellationToken)
    {
        var binary = string.IsNullOrWhiteSpace(rcloneBinaryPath) ? "rclone" : rcloneBinaryPath;

        var result = await Cli.Wrap(binary)
            .WithArguments(["config", "providers"])
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError)
                ? "Could not read rclone backends."
                : result.StandardError.Trim());
        }

        var providers = JsonSerializer.Deserialize<List<ProviderDto>>(result.StandardOutput);
        if (providers is null)
        {
            return [];
        }

        return providers
            .Where(p => !p.Hide)
            .Select(p => new RcloneBackendInfo
            {
                Name = p.Name,
                Description = p.Description,
                Options = p.Options
                    .Where(o => o is not null)
                    .Select(o => new RcloneBackendOption
                    {
                        Name = o.Name,
                        Help = o.Help,
                        Required = o.Required,
                        IsPassword = o.IsPassword,
                        Advanced = o.Advanced,
                    })
                    .Where(o => !string.IsNullOrWhiteSpace(o.Name))
                    .ToList(),
            })
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task CreateRemoteAsync(
        string rcloneBinaryPath,
        string remoteName,
        string backendName,
        IEnumerable<RcloneBackendOptionInput> options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(remoteName))
        {
            throw new InvalidOperationException("Remote name is required.");
        }

        if (string.IsNullOrWhiteSpace(backendName))
        {
            throw new InvalidOperationException("Backend is required.");
        }

        var binary = string.IsNullOrWhiteSpace(rcloneBinaryPath) ? "rclone" : rcloneBinaryPath;

        var args = new List<string> { "config", "create", remoteName.Trim(), backendName.Trim() };
        foreach (var option in options)
        {
            if (string.IsNullOrWhiteSpace(option.Name) || string.IsNullOrWhiteSpace(option.Value))
            {
                continue;
            }

            args.Add(option.Name);
            args.Add(option.Value);
        }

        var result = await Cli.Wrap(binary)
            .WithArguments(args)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);

        if (result.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;
            throw new InvalidOperationException($"Failed to create remote: {error.Trim()}");
        }
    }

    private sealed class ProviderDto
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Hide { get; set; }
        public List<OptionDto> Options { get; set; } = [];
    }

    private sealed class OptionDto
    {
        public string Name { get; set; } = string.Empty;
        public string Help { get; set; } = string.Empty;
        public bool Required { get; set; }
        public bool IsPassword { get; set; }
        public bool Advanced { get; set; }
    }
}
