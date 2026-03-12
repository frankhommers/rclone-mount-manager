using CliWrap;
using CliWrap.Buffered;
using Microsoft.Extensions.Logging;
using RcloneMountManager.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RcloneMountManager.Core.Services;

public sealed class RcloneBackendService
{
  private readonly ILogger<RcloneBackendService> _logger;

  public RcloneBackendService(ILogger<RcloneBackendService> logger)
  {
    _logger = logger;
  }

  public async Task<IReadOnlyList<RcloneBackendInfo>> GetBackendsAsync(
    string rcloneBinaryPath,
    CancellationToken cancellationToken)
  {
    string binary = string.IsNullOrWhiteSpace(rcloneBinaryPath) ? "rclone" : rcloneBinaryPath;

    BufferedCommandResult result = await Cli.Wrap(binary)
      .WithArguments(["config", "providers"])
      .WithValidation(CommandResultValidation.None)
      .ExecuteBufferedAsync(cancellationToken);

    if (result.ExitCode != 0)
    {
      throw new InvalidOperationException(
        string.IsNullOrWhiteSpace(result.StandardError)
          ? "Could not read rclone backends."
          : result.StandardError.Trim());
    }

    List<ProviderDto>? providers = JsonSerializer.Deserialize<List<ProviderDto>>(result.StandardOutput);
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
        RequiresOAuth = p.Options.Any(o =>
                                        string.Equals(o.Name, "token", StringComparison.OrdinalIgnoreCase)),
        Options = p.Options
          .Where(o => o is not null)
          .Select(o => new RcloneBackendOption
          {
            Name = o.Name,
            Help = o.Help,
            Type = o.Type,
            DefaultStr = o.DefaultStr,
            Required = o.Required,
            IsPassword = o.IsPassword,
            Advanced = o.Advanced,
            Examples = o.Examples?.Select(e => e.Value).Where(v => !string.IsNullOrEmpty(v)).ToList(),
          })
          .Where(o => !string.IsNullOrWhiteSpace(o.Name))
          .DistinctBy(o => o.Name)
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

    string binary = string.IsNullOrWhiteSpace(rcloneBinaryPath) ? "rclone" : rcloneBinaryPath;

    List<RcloneBackendOptionInput> optionList = options.ToList();
    List<string> args = new() {"config", "create", remoteName.Trim(), backendName.Trim()};
    foreach (RcloneBackendOptionInput option in optionList)
    {
      if (string.IsNullOrWhiteSpace(option.Name) || string.IsNullOrWhiteSpace(option.Value))
      {
        continue;
      }

      args.Add(option.Name);
      args.Add(option.Value);
    }

    if (optionList.Any(o => o.IsPassword && !string.IsNullOrWhiteSpace(o.Value)))
    {
      args.Add("--obscure");
    }

    BufferedCommandResult result = await Cli.Wrap(binary)
      .WithArguments(args)
      .WithValidation(CommandResultValidation.None)
      .ExecuteBufferedAsync(cancellationToken);

    if (result.ExitCode != 0)
    {
      string error = string.IsNullOrWhiteSpace(result.StandardError)
        ? result.StandardOutput
        : result.StandardError;
      throw new InvalidOperationException($"Failed to create remote: {error.Trim()}");
    }
  }

  public async Task UpdateRemoteAsync(
    string rcloneBinaryPath,
    string remoteName,
    IEnumerable<RcloneBackendOptionInput> options,
    CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(remoteName))
    {
      throw new InvalidOperationException("Remote name is required.");
    }

    string binary = string.IsNullOrWhiteSpace(rcloneBinaryPath) ? "rclone" : rcloneBinaryPath;

    List<RcloneBackendOptionInput> optionList = options.ToList();
    List<string> args = new() {"config", "update", remoteName.Trim()};
    foreach (RcloneBackendOptionInput option in optionList)
    {
      if (string.IsNullOrWhiteSpace(option.Name) || string.IsNullOrWhiteSpace(option.Value))
      {
        continue;
      }

      args.Add(option.Name);
      args.Add(option.Value);
    }

    if (optionList.Any(o => o.IsPassword && !string.IsNullOrWhiteSpace(o.Value)))
    {
      args.Add("--obscure");
    }

    args.Add("--non-interactive");

    BufferedCommandResult result = await Cli.Wrap(binary)
      .WithArguments(args)
      .WithValidation(CommandResultValidation.None)
      .ExecuteBufferedAsync(cancellationToken);

    if (result.ExitCode != 0)
    {
      string error = string.IsNullOrWhiteSpace(result.StandardError)
        ? result.StandardOutput
        : result.StandardError;
      throw new InvalidOperationException($"Failed to update remote: {error.Trim()}");
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
    public string Type { get; set; } = "string";
    public string DefaultStr { get; set; } = string.Empty;
    public List<ExampleDto>? Examples { get; set; }
    public bool Required { get; set; }
    public bool IsPassword { get; set; }
    public bool Advanced { get; set; }
  }

  private sealed class ExampleDto
  {
    public string Value { get; set; } = string.Empty;
    public string Help { get; set; } = string.Empty;
  }
}