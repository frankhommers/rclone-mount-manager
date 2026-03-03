using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CliWrap;
using CliWrap.Buffered;
using Microsoft.Extensions.Logging;
using RcloneMountManager.Core.Models;

namespace RcloneMountManager.Core.Services;

public sealed class RcloneConfigWizardService
{
  private readonly ILogger<RcloneConfigWizardService> _logger;

  public RcloneConfigWizardService(ILogger<RcloneConfigWizardService> logger)
  {
    _logger = logger;
  }

  public async Task<ConfigWizardStep> StartAsync(
    string rcloneBinaryPath,
    string remoteName,
    string backendName,
    CancellationToken cancellationToken)
  {
    var binary = ResolveBinary(rcloneBinaryPath);
    var result = await Cli.Wrap(binary)
      .WithArguments(["config", "create", remoteName.Trim(), backendName.Trim(), "--non-interactive"])
      .WithValidation(CommandResultValidation.None)
      .ExecuteBufferedAsync(cancellationToken);

    if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(result.StandardOutput))
    {
      throw new InvalidOperationException($"Wizard failed: {result.StandardError.Trim()}");
    }

    return ParseStep(result.StandardOutput);
  }

  public async Task<ConfigWizardStep> ContinueAsync(
    string rcloneBinaryPath,
    string remoteName,
    string state,
    string answer,
    CancellationToken cancellationToken)
  {
    var binary = ResolveBinary(rcloneBinaryPath);
    var result = await Cli.Wrap(binary)
      .WithArguments(["config", "update", remoteName.Trim(),
        "--continue", "--state", state, "--result", answer, "--non-interactive"])
      .WithValidation(CommandResultValidation.None)
      .ExecuteBufferedAsync(cancellationToken);

    if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(result.StandardOutput))
    {
      throw new InvalidOperationException($"Wizard step failed: {result.StandardError.Trim()}");
    }

    return ParseStep(result.StandardOutput);
  }

  public async Task<ConfigWizardStep> ContinueOAuthAsync(
    string rcloneBinaryPath,
    string remoteName,
    string state,
    Action<string> onAuthUrl,
    CancellationToken cancellationToken)
  {
    var binary = ResolveBinary(rcloneBinaryPath);
    var stdoutBuilder = new StringBuilder();
    var authUrlDetected = false;

    var command = Cli.Wrap(binary)
      .WithArguments(["config", "update", remoteName.Trim(),
        "--continue", "--state", state, "--result", "true", "--non-interactive"])
      .WithValidation(CommandResultValidation.None)
      .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdoutBuilder))
      .WithStandardErrorPipe(PipeTarget.ToDelegate(line =>
      {
        _logger.LogDebug("OAuth stderr: {Line}", line);
        if (!authUrlDetected && TryExtractAuthUrl(line, out string? url))
        {
          authUrlDetected = true;
          onAuthUrl(url!);
        }
      }));

    var result = await command.ExecuteAsync(cancellationToken);

    if (result.ExitCode != 0 && stdoutBuilder.Length == 0)
    {
      throw new InvalidOperationException("OAuth authorization failed or was cancelled.");
    }

    return ParseStep(stdoutBuilder.ToString());
  }

  public async Task<Dictionary<string, string>> ReadRemoteConfigAsync(
    string rcloneBinaryPath,
    string remoteName,
    CancellationToken cancellationToken)
  {
    var binary = ResolveBinary(rcloneBinaryPath);
    var result = await Cli.Wrap(binary)
      .WithArguments(["config", "dump"])
      .WithValidation(CommandResultValidation.None)
      .ExecuteBufferedAsync(cancellationToken);

    if (result.ExitCode != 0)
    {
      throw new InvalidOperationException($"Failed to read config: {result.StandardError.Trim()}");
    }

    var dump = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(result.StandardOutput);
    if (dump is not null && dump.TryGetValue(remoteName.Trim(), out Dictionary<string, string>? config))
    {
      return config;
    }

    return new Dictionary<string, string>();
  }

  public async Task DeleteRemoteAsync(
    string rcloneBinaryPath,
    string remoteName,
    CancellationToken cancellationToken)
  {
    var binary = ResolveBinary(rcloneBinaryPath);
    await Cli.Wrap(binary)
      .WithArguments(["config", "delete", remoteName.Trim()])
      .WithValidation(CommandResultValidation.None)
      .ExecuteBufferedAsync(cancellationToken);
  }

  private static string ResolveBinary(string rcloneBinaryPath) =>
    string.IsNullOrWhiteSpace(rcloneBinaryPath) ? "rclone" : rcloneBinaryPath;

  private ConfigWizardStep ParseStep(string json)
  {
    if (string.IsNullOrWhiteSpace(json))
    {
      return new ConfigWizardStep();
    }

    var dto = JsonSerializer.Deserialize<WizardResponseDto>(json, JsonOptions);
    if (dto is null)
    {
      return new ConfigWizardStep();
    }

    if (string.IsNullOrEmpty(dto.State) || dto.Option is null)
    {
      return new ConfigWizardStep();
    }

    return new ConfigWizardStep
    {
      State = dto.State,
      Name = dto.Option.Name,
      Help = dto.Option.Help,
      Type = dto.Option.Type,
      DefaultValue = dto.Option.DefaultStr ?? dto.Option.ValueStr ?? string.Empty,
      Required = dto.Option.Required,
      IsPassword = dto.Option.IsPassword,
      Exclusive = dto.Option.Exclusive,
      Error = dto.Error ?? string.Empty,
      Examples = dto.Option.Examples?
        .Select(e => new ConfigWizardExample { Value = e.Value, Help = e.Help })
        .ToList() ?? [],
    };
  }

  private static bool TryExtractAuthUrl(string line, out string? url)
  {
    var match = Regex.Match(line, @"(http://127\.0\.0\.1:\d+/auth\S+)");
    if (match.Success)
    {
      url = match.Groups[1].Value;
      return true;
    }

    url = null;
    return false;
  }

  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
  };

  private sealed class WizardResponseDto
  {
    public string State { get; set; } = string.Empty;
    public WizardOptionDto? Option { get; set; }
    public string Error { get; set; } = string.Empty;
  }

  private sealed class WizardOptionDto
  {
    public string Name { get; set; } = string.Empty;
    public string Help { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public string DefaultStr { get; set; } = string.Empty;
    public string ValueStr { get; set; } = string.Empty;
    public bool Required { get; set; }
    public bool IsPassword { get; set; }
    public bool Exclusive { get; set; }
    public List<WizardExampleDto>? Examples { get; set; }
  }

  private sealed class WizardExampleDto
  {
    public string Value { get; set; } = string.Empty;
    public string Help { get; set; } = string.Empty;
  }
}
