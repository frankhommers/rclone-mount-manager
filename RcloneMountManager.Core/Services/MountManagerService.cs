using CliWrap;
using CliWrap.Buffered;
using Microsoft.Extensions.Logging;
using RcloneMountManager.Core.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RcloneMountManager.Core.Services;

public sealed class MountManagerService
{
  private readonly ILogger<MountManagerService> _logger;
  private readonly ConcurrentDictionary<string, RunningMount> _runningMounts = new(StringComparer.OrdinalIgnoreCase);

  private readonly ConcurrentDictionary<string, string>
    _rcloneMountCommandCache = new(StringComparer.OrdinalIgnoreCase);

  public MountManagerService(ILogger<MountManagerService> logger)
  {
    _logger = logger;
  }

  public async Task StartAsync(MountProfile profile, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(profile);

    string mountPoint = ResolveMountPoint(profile.MountPoint);

    if (string.IsNullOrWhiteSpace(profile.Source) &&
        !(IsRcloneMountType(profile.Type) && profile.QuickConnectMode is not QuickConnectMode.None))
    {
      throw new InvalidOperationException("Source is required.");
    }

    if (string.IsNullOrWhiteSpace(mountPoint))
    {
      throw new InvalidOperationException("Mount point is required.");
    }

    try
    {
      Directory.CreateDirectory(mountPoint);
    }
    catch (UnauthorizedAccessException ex)
    {
      string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
      string suggestion = Path.Combine(home, "Mounts", "my-mount");
      throw new InvalidOperationException(
        $"No write access to mount path '{profile.MountPoint}'. Choose a folder inside your home directory, for example '{suggestion}'.",
        ex);
    }

    if (!string.Equals(profile.MountPoint, mountPoint, StringComparison.Ordinal))
    {
      _logger.LogInformation(
        "Resolved mount path '{OriginalPath}' -> '{ResolvedPath}'",
        profile.MountPoint,
        mountPoint);
    }

    if (IsRcloneMountType(profile.Type))
    {
      await StartRcloneAsync(profile, mountPoint, cancellationToken);
      return;
    }

    await StartNfsAsync(profile, mountPoint, cancellationToken);
  }

  public async Task StopAsync(MountProfile profile, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(profile);

    string mountPoint = ResolveMountPoint(profile.MountPoint);

    if (IsRcloneMountType(profile.Type) && _runningMounts.TryRemove(mountPoint, out RunningMount? runningMount))
    {
      if (runningMount.RcPort > 0)
      {
        _logger.LogInformation("Sending quit via RC...");
        RcloneRcClient rcClient = new(new HttpClient());
        await rcClient.QuitAsync(runningMount.RcPort, cancellationToken);

        for (int attempt = 0; attempt < 10; attempt++)
        {
          await Task.Delay(500, cancellationToken);
          if (!await IsMountedAsync(mountPoint, cancellationToken))
          {
            _logger.LogInformation("rclone stopped via RC");
            return;
          }
        }

        _logger.LogWarning("RC quit timed out, falling back to umount");
      }
      else
      {
        _logger.LogInformation("No RC port, falling back to umount");
      }
    }
    else if (IsRcloneMountType(profile.Type))
    {
      if (profile.EnableRemoteControl && profile.RcPort > 0)
      {
        _logger.LogInformation("No tracked process; trying RC quit for orphan...");
        RcloneRcClient rcClient = new(new HttpClient());
        if (await rcClient.QuitAsync(profile.RcPort, cancellationToken))
        {
          for (int attempt = 0; attempt < 10; attempt++)
          {
            await Task.Delay(500, cancellationToken);
            if (!await IsMountedAsync(mountPoint, cancellationToken))
            {
              _logger.LogInformation("Orphan rclone stopped via RC");
              return;
            }
          }
        }
      }

      _logger.LogInformation("No tracked rclone process found; attempting unmount of orphan mount");
    }

    await UnmountAsync(mountPoint, cancellationToken);
  }

  public async Task<bool> IsMountedAsync(string mountPoint, CancellationToken cancellationToken)
  {
    mountPoint = ResolveMountPoint(mountPoint);

    BufferedCommandResult result = await Cli.Wrap("mount")
      .WithValidation(CommandResultValidation.None)
      .ExecuteBufferedAsync(cancellationToken);

    if (result.ExitCode != 0)
    {
      return false;
    }

    string target = $" on {mountPoint}";
    return result.StandardOutput.Contains(target, StringComparison.Ordinal);
  }

  public bool IsRunning(string mountPoint)
  {
    return _runningMounts.ContainsKey(ResolveMountPoint(mountPoint));
  }

  public async Task TestConnectionAsync(MountProfile profile, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(profile);

    if (profile.Type is MountType.MacOsNfs)
    {
      throw new InvalidOperationException("Connectivity test currently supports rclone profiles only.");
    }

    string binary = string.IsNullOrWhiteSpace(profile.RcloneBinaryPath) ? "rclone" : profile.RcloneBinaryPath;

    List<string> arguments;
    if (profile.QuickConnectMode is QuickConnectMode.None)
    {
      string target = BuildConnectivityTarget(profile.Source);
      arguments = new List<string> {"lsd", target, "--max-depth", "1", "-vv"};
    }
    else
    {
      string source = ResolveRcloneSource(profile);
      arguments = new List<string> {"lsd", source, "--max-depth", "1", "-vv"};
      await AddQuickConnectArgumentsAsync(profile, arguments, cancellationToken);
    }

    BufferedCommandResult result = await Cli.Wrap(binary)
      .WithArguments(arguments)
      .WithValidation(CommandResultValidation.None)
      .ExecuteBufferedAsync(cancellationToken);

    if (!string.IsNullOrWhiteSpace(result.StandardOutput))
    {
      _logger.LogInformation("{Output}", result.StandardOutput.Trim());
    }

    if (!string.IsNullOrWhiteSpace(result.StandardError))
    {
      foreach (string line in result.StandardError.Split('\n', StringSplitOptions.RemoveEmptyEntries))
      {
        string trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
          continue;
        }

        LogClassifiedStderrLine(trimmed);
      }
    }

    if (result.ExitCode != 0)
    {
      throw new InvalidOperationException($"Connectivity test failed with exit code {result.ExitCode}.");
    }

    _logger.LogInformation("Connectivity test succeeded");
  }

  public async Task TestBackendConnectionAsync(
    string rcloneBinaryPath,
    string backendName,
    IEnumerable<RcloneBackendOptionInput> options,
    CancellationToken cancellationToken)
  {
    string binary = string.IsNullOrWhiteSpace(rcloneBinaryPath) ? "rclone" : rcloneBinaryPath;
    List<string> arguments = new() {"lsd", $":{backendName}:", "--max-depth", "1", "-vv"};

    HashSet<string> secretFlags = new(StringComparer.OrdinalIgnoreCase);

    foreach (RcloneBackendOptionInput option in options)
    {
      if (string.IsNullOrWhiteSpace(option.Name) || string.IsNullOrWhiteSpace(option.Value))
      {
        continue;
      }

      string flagName = option.Name.Replace('_', '-');
      string flag = $"--{backendName}-{flagName}";

      if (option.ControlType == OptionControlType.Toggle)
      {
        if (string.Equals(option.Value, "true", StringComparison.OrdinalIgnoreCase))
        {
          arguments.Add(flag);
        }

        continue;
      }

      string flagValue = option.Value;
      if (option.IsPassword)
      {
        secretFlags.Add(flag);
        BufferedCommandResult obscureResult = await Cli.Wrap(binary)
          .WithArguments(["obscure", option.Value])
          .WithValidation(CommandResultValidation.None)
          .ExecuteBufferedAsync(cancellationToken);

        if (obscureResult.ExitCode != 0)
        {
          throw new InvalidOperationException($"Failed to obscure password for '{option.Name}'.");
        }

        flagValue = obscureResult.StandardOutput.Trim();
      }

      arguments.Add(flag);
      arguments.Add(flagValue);
    }

    HashSet<string> secretValues = CollectSecretValues(arguments, secretFlags);

    _logger.LogInformation("$ {Binary} {CommandLine}", binary, FormatCommandLine(arguments, secretFlags));

    BufferedCommandResult result = await Cli.Wrap(binary)
      .WithArguments(arguments)
      .WithValidation(CommandResultValidation.None)
      .ExecuteBufferedAsync(cancellationToken);

    if (!string.IsNullOrWhiteSpace(result.StandardOutput))
    {
      _logger.LogInformation("{Output}", RedactSecrets(result.StandardOutput.Trim(), secretValues));
    }

    if (!string.IsNullOrWhiteSpace(result.StandardError))
    {
      foreach (string line in result.StandardError.Split('\n', StringSplitOptions.RemoveEmptyEntries))
      {
        string trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
          continue;
        }

        string redacted = RedactSecrets(trimmed, secretValues);
        LogClassifiedStderrLine(redacted);
      }
    }

    if (result.ExitCode != 0)
    {
      throw new InvalidOperationException($"Connectivity test failed with exit code {result.ExitCode}.");
    }

    _logger.LogInformation("Connectivity test succeeded");
  }

  public string GenerateScript(MountProfile profile)
  {
    StringBuilder builder = new();
    builder.AppendLine("#!/usr/bin/env bash");
    builder.AppendLine("set -euo pipefail");
    builder.AppendLine();
    builder.AppendLine($"MOUNT_POINT=\"{EscapeForBash(ResolveMountPointForScript(profile.MountPoint))}\"");
    builder.AppendLine();
    builder.AppendLine("mkdir -p \"$MOUNT_POINT\"");

    if (IsRcloneMountType(profile.Type))
    {
      string binary = string.IsNullOrWhiteSpace(profile.RcloneBinaryPath) ? "rclone" : profile.RcloneBinaryPath;
      string mountCommand = GetRcloneMountCommandForScript(binary, profile.Type);
      string source = ResolveRcloneSource(profile);

      builder.Append("\"");
      builder.Append(EscapeForBash(binary));
      builder.Append("\" ");
      builder.Append(mountCommand);
      builder.Append(' ');
      builder.Append(EscapeArgument(source));
      builder.Append(" \"$MOUNT_POINT\"");
      AppendQuickConnectScriptArgs(profile, builder);

      IReadOnlyList<string> options = ParseArguments(profile.ExtraOptions);
      foreach (string option in options)
      {
        builder.Append(' ');
        builder.Append(EscapeArgument(option));
      }

      foreach (KeyValuePair<string, string> kvp in profile.MountOptions)
      {
        string flag = "--" + kvp.Key.Replace('_', '-');
        if (string.Equals(kvp.Value, "true", StringComparison.OrdinalIgnoreCase))
        {
          builder.Append(' ');
          builder.Append(EscapeArgument(flag));
        }
        else if (string.Equals(kvp.Value, "false", StringComparison.OrdinalIgnoreCase))
        {
          // Skip false booleans
        }
        else if (!string.IsNullOrEmpty(kvp.Value))
        {
          builder.Append(' ');
          builder.Append(EscapeArgument(flag));
          builder.Append(' ');
          builder.Append(EscapeArgument(kvp.Value));
        }
      }

      if (profile.EnableRemoteControl && profile.RcPort > 0)
      {
        bool hasRcAddr = profile.MountOptions.ContainsKey("rc_addr") ||
                         profile.ExtraOptions.Contains("--rc-addr", StringComparison.OrdinalIgnoreCase);
        if (!hasRcAddr)
        {
          builder.Append(" --rc --rc-no-auth --rc-addr ");
          builder.Append(EscapeArgument($"localhost:{profile.RcPort}"));
        }
      }

      builder.AppendLine();
    }
    else
    {
      builder.Append("mount -t nfs ");
      if (!string.IsNullOrWhiteSpace(profile.ExtraOptions))
      {
        builder.Append("-o ");
        builder.Append(EscapeArgument(profile.ExtraOptions));
        builder.Append(' ');
      }

      builder.Append(EscapeArgument(profile.Source));
      builder.Append(" \"$MOUNT_POINT\"");
      builder.AppendLine();
    }

    return builder.ToString();
  }

  private async Task StartRcloneAsync(MountProfile profile, string mountPoint, CancellationToken cancellationToken)
  {
    if (_runningMounts.ContainsKey(mountPoint))
    {
      throw new InvalidOperationException("rclone mount is already running for this mount point.");
    }

    if (await IsMountedAsync(mountPoint, cancellationToken))
    {
      if (_runningMounts.ContainsKey(mountPoint))
      {
        _logger.LogInformation("Mount point is already active and tracked");
        return;
      }

      throw new InvalidOperationException(
        $"Mount point '{mountPoint}' is already in use (possibly from a previous session). Stop the existing mount first.");
    }

    string source = ResolveRcloneSource(profile);
    string binary = string.IsNullOrWhiteSpace(profile.RcloneBinaryPath) ? "rclone" : profile.RcloneBinaryPath;
    string mountCommand = await ResolveRcloneMountCommandAsync(binary, profile.Type, cancellationToken);
    List<string> arguments = new() {mountCommand, source, mountPoint};
    await AddQuickConnectArgumentsAsync(profile, arguments, cancellationToken);

    arguments.AddRange(ParseArguments(profile.ExtraOptions));

    foreach (KeyValuePair<string, string> kvp in profile.MountOptions)
    {
      string flag = "--" + kvp.Key.Replace('_', '-');
      if (string.Equals(kvp.Value, "true", StringComparison.OrdinalIgnoreCase))
      {
        arguments.Add(flag);
      }
      else if (string.Equals(kvp.Value, "false", StringComparison.OrdinalIgnoreCase))
      {
        // Skip false booleans (they are the default)
      }
      else if (!string.IsNullOrEmpty(kvp.Value))
      {
        arguments.Add(flag);
        arguments.Add(kvp.Value);
      }
    }

    bool rcEnabled = profile.EnableRemoteControl && profile.RcPort > 0;
    if (rcEnabled)
    {
      bool hasRcAddr = arguments.Any(a => a.StartsWith("--rc-addr", StringComparison.OrdinalIgnoreCase));
      if (!hasRcAddr)
      {
        arguments.Add("--rc");
        arguments.Add("--rc-no-auth");
        arguments.Add("--rc-addr");
        arguments.Add($"localhost:{profile.RcPort}");
      }
    }

    string logDir = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
      "RcloneMountManager",
      "logs");
    Directory.CreateDirectory(logDir);
    string logFile = Path.Combine(logDir, $"{profile.Id}.log");

    _logger.LogInformation(
      "Launching detached: {Binary} {MountCommand} {Source} {MountPoint}",
      binary,
      mountCommand,
      source,
      mountPoint);

    FileStream logStream = new(logFile, FileMode.Create, FileAccess.Write, FileShare.Read);
    Command command = Cli.Wrap(binary)
      .WithArguments(arguments)
      .WithStandardOutputPipe(PipeTarget.ToStream(logStream))
      .WithStandardErrorPipe(PipeTarget.ToStream(logStream))
      .WithValidation(CommandResultValidation.None);

    _ = Task.Run(
      async () =>
      {
        try
        {
          await command.ExecuteAsync(CancellationToken.None);
        }
        catch
        {
        }
        finally
        {
          await logStream.DisposeAsync();
          _runningMounts.TryRemove(mountPoint, out _);
        }
      },
      CancellationToken.None);

    if (rcEnabled)
    {
      RcloneRcClient rcClient = new(new HttpClient());
      int? pid = null;
      for (int attempt = 0; attempt < 10; attempt++)
      {
        await Task.Delay(500, cancellationToken);
        pid = await rcClient.GetPidAsync(profile.RcPort, cancellationToken);
        if (pid.HasValue)
        {
          break;
        }
      }

      if (!pid.HasValue)
      {
        string logTail = ReadLogTail(logFile);
        string detail = ExtractRcloneErrorDetail(logTail);
        _logger.LogWarning(
          "rclone launched but RC not responding on port {RcPort}. Check log: {LogFile}",
          profile.RcPort,
          logFile);
        throw new InvalidOperationException(
          string.IsNullOrWhiteSpace(detail)
            ? $"rclone failed to start. Check log: {logFile}"
            : detail);
      }

      _logger.LogInformation(
        "rclone process started (PID {Pid}, RC port {RcPort}). Waiting for mount...",
        pid.Value,
        profile.RcPort);

      bool mounted = false;
      for (int attempt = 0; attempt < 20; attempt++)
      {
        await Task.Delay(500, cancellationToken);
        if (await IsMountedAsync(mountPoint, cancellationToken))
        {
          mounted = true;
          break;
        }

        if (!await rcClient.IsAliveAsync(profile.RcPort, cancellationToken))
        {
          string earlyLogTail = ReadLogTail(logFile);
          string earlyDetail = ExtractRcloneErrorDetail(earlyLogTail);
          _logger.LogError("rclone process died before mount appeared. Check log: {LogFile}", logFile);
          throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(earlyDetail)
              ? $"rclone process died before mount appeared. Check log: {logFile}"
              : earlyDetail);
        }
      }

      if (mounted)
      {
        _runningMounts.TryAdd(mountPoint, new RunningMount(pid.Value, profile.RcPort));
        _logger.LogInformation(
          "rclone {MountCommand} started and mounted (PID {Pid}, RC port {RcPort})",
          mountCommand,
          pid.Value,
          profile.RcPort);
      }
      else
      {
        string lateLogTail = ReadLogTail(logFile);
        string lateDetail = ExtractRcloneErrorDetail(lateLogTail);
        _logger.LogWarning(
          "rclone running (PID {Pid}) but mount not appearing. Check log: {LogFile}",
          pid.Value,
          logFile);
        throw new InvalidOperationException(
          string.IsNullOrWhiteSpace(lateDetail)
            ? $"rclone is running but mount did not appear. Check log: {logFile}"
            : lateDetail);
      }
    }
    else
    {
      await Task.Delay(2000, cancellationToken);
      if (await IsMountedAsync(mountPoint, cancellationToken))
      {
        _logger.LogInformation("rclone {MountCommand} started (no RC, mount point verified)", mountCommand);
        _runningMounts.TryAdd(mountPoint, new RunningMount(0, 0));
      }
      else
      {
        throw new InvalidOperationException($"Mount did not appear after launch. Check the log file: {logFile}");
      }
    }
  }

  private async Task StartNfsAsync(MountProfile profile, string mountPoint, CancellationToken cancellationToken)
  {
    if (!string.IsNullOrWhiteSpace(profile.ExtraOptions) &&
        profile.ExtraOptions.Contains("--", StringComparison.Ordinal))
    {
      throw new InvalidOperationException(
        "NFS options look like rclone flags (contain '--'). Use NFS options such as 'nfsvers=4,resvport' or switch type to Rclone.");
    }

    List<string> arguments = new() {"-t", "nfs"};

    if (!string.IsNullOrWhiteSpace(profile.ExtraOptions))
    {
      arguments.Add("-o");
      arguments.Add(profile.ExtraOptions);
    }

    arguments.Add(profile.Source);
    arguments.Add(mountPoint);

    BufferedCommandResult result = await Cli.Wrap("mount")
      .WithArguments(arguments)
      .WithValidation(CommandResultValidation.None)
      .ExecuteBufferedAsync(cancellationToken);

    if (!string.IsNullOrWhiteSpace(result.StandardOutput))
    {
      _logger.LogInformation("{Output}", result.StandardOutput.Trim());
    }

    if (!string.IsNullOrWhiteSpace(result.StandardError))
    {
      _logger.LogError("{Output}", result.StandardError.Trim());
    }

    if (result.ExitCode != 0)
    {
      throw new InvalidOperationException($"NFS mount failed with exit code {result.ExitCode}.");
    }
  }

  private async Task UnmountAsync(string mountPoint, CancellationToken cancellationToken)
  {
    if (!await IsMountedAsync(mountPoint, cancellationToken))
    {
      _logger.LogInformation("Mount point is not mounted");
      return;
    }

    (string Binary, string[] Args)[] unmountCandidates = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
      ? new[]
      {
        (Binary: "fusermount", Args: new[] {"-u", mountPoint}),
        (Binary: "umount", Args: new[] {mountPoint}),
      }
      : new[]
      {
        (Binary: "diskutil", Args: new[] {"unmount", mountPoint}),
        (Binary: "umount", Args: new[] {mountPoint}),
        (Binary: "diskutil", Args: new[] {"unmount", "force", mountPoint}),
        (Binary: "umount", Args: new[] {"-f", mountPoint}),
      };

    foreach ((string Binary, string[] Args) candidate in unmountCandidates)
    {
      BufferedCommandResult result = await Cli.Wrap(candidate.Binary)
        .WithArguments(candidate.Args)
        .WithValidation(CommandResultValidation.None)
        .ExecuteBufferedAsync(cancellationToken);

      if (result.ExitCode == 0)
      {
        _logger.LogInformation(
          "Unmounted {MountPoint} via {Binary} {Args}",
          mountPoint,
          candidate.Binary,
          string.Join(" ", candidate.Args));
        return;
      }

      _logger.LogDebug(
        "Unmount attempt failed: {Binary} {Args} (exit {ExitCode}): {Error}",
        candidate.Binary,
        string.Join(" ", candidate.Args),
        result.ExitCode,
        result.StandardError.Trim());
    }

    throw new InvalidOperationException($"Could not unmount {mountPoint}. Try unmounting manually.");
  }

  private static string ResolveMountPoint(string mountPoint)
  {
    if (string.IsNullOrWhiteSpace(mountPoint))
    {
      return mountPoint;
    }

    string trimmed = mountPoint.Trim();
    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    if (trimmed == "~")
    {
      return home;
    }

    if (trimmed.StartsWith("~/", StringComparison.Ordinal))
    {
      return Path.Combine(home, trimmed[2..]);
    }

    return trimmed;
  }

  private static string ResolveMountPointForScript(string mountPoint)
  {
    if (string.IsNullOrWhiteSpace(mountPoint))
    {
      return mountPoint;
    }

    string trimmed = mountPoint.Trim();
    if (trimmed == "~")
    {
      return "$HOME";
    }

    if (trimmed.StartsWith("~/", StringComparison.Ordinal))
    {
      return "$HOME/" + trimmed[2..];
    }

    return trimmed;
  }

  private static string BuildConnectivityTarget(string source)
  {
    if (string.IsNullOrWhiteSpace(source))
    {
      throw new InvalidOperationException("Source is required for connectivity test.");
    }

    string trimmed = source.Trim();
    int colonIndex = trimmed.IndexOf(':');
    if (colonIndex <= 0)
    {
      return trimmed;
    }

    return trimmed[..(colonIndex + 1)];
  }

  private static IReadOnlyList<string> ParseArguments(string input)
  {
    List<string> args = new();
    if (string.IsNullOrWhiteSpace(input))
    {
      return args;
    }

    StringBuilder current = new();
    bool inQuotes = false;

    foreach (char ch in input)
    {
      if (ch == '"')
      {
        inQuotes = !inQuotes;
        continue;
      }

      if (!inQuotes && char.IsWhiteSpace(ch))
      {
        if (current.Length > 0)
        {
          args.Add(current.ToString());
          current.Clear();
        }

        continue;
      }

      current.Append(ch);
    }

    if (current.Length > 0)
    {
      args.Add(current.ToString());
    }

    return args;
  }

  private static string FormatCommandLine(List<string> arguments, HashSet<string> secretFlags)
  {
    List<string> parts = new();
    for (int i = 0; i < arguments.Count; i++)
    {
      string arg = arguments[i];
      if (secretFlags.Contains(arg) && i + 1 < arguments.Count)
      {
        parts.Add(arg);
        parts.Add("****");
        i++;
      }
      else
      {
        parts.Add(arg.Contains(' ') ? $"\"{arg}\"" : arg);
      }
    }

    return string.Join(" ", parts);
  }

  private static HashSet<string> CollectSecretValues(List<string> arguments, HashSet<string> secretFlags)
  {
    HashSet<string> values = new(StringComparer.Ordinal);
    for (int i = 0; i < arguments.Count; i++)
    {
      if (secretFlags.Contains(arguments[i]) && i + 1 < arguments.Count)
      {
        string secretValue = arguments[i + 1];
        if (!string.IsNullOrWhiteSpace(secretValue))
        {
          values.Add(secretValue);
        }

        i++;
      }
    }

    return values;
  }

  private static string RedactSecrets(string text, HashSet<string> secretValues)
  {
    foreach (string secret in secretValues)
    {
      text = text.Replace(secret, "****", StringComparison.Ordinal);
    }

    return text;
  }

  private static string EscapeForBash(string value)
  {
    return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
  }

  private static string EscapeArgument(string value)
  {
    string escaped = value.Replace("'", "'\"'\"'");
    return $"'{escaped}'";
  }

  private static string ResolveRcloneSource(MountProfile profile)
  {
    if (profile.QuickConnectMode is not QuickConnectMode.None)
    {
      string subPath = string.IsNullOrWhiteSpace(profile.Source) ? "/" : profile.Source.Trim();
      if (!subPath.StartsWith("/", StringComparison.Ordinal))
      {
        subPath = "/" + subPath;
      }

      string backend = profile.QuickConnectMode switch
      {
        QuickConnectMode.WebDav => "webdav",
        QuickConnectMode.Sftp => "sftp",
        QuickConnectMode.Ftp => "ftp",
        QuickConnectMode.Ftps => "ftp",
        _ => "",
      };

      return $":{backend}:{subPath}";
    }

    return profile.Source;
  }

  private static void AppendQuickConnectScriptArgs(MountProfile profile, StringBuilder builder)
  {
    switch (profile.QuickConnectMode)
    {
      case QuickConnectMode.None:
        return;
      case QuickConnectMode.WebDav:
        EnsureNotEmpty(profile.QuickConnectEndpoint, "WebDAV URL is required.");
        builder.Append(" --webdav-url ");
        builder.Append(EscapeArgument(profile.QuickConnectEndpoint));
        builder.Append(" --webdav-vendor ");
        builder.Append(EscapeArgument("other"));
        if (!string.IsNullOrWhiteSpace(profile.QuickConnectUsername))
        {
          builder.Append(" --webdav-user ");
          builder.Append(EscapeArgument(profile.QuickConnectUsername));
        }

        builder.Append(" --webdav-pass ");
        builder.Append(BuildScriptPasswordValue(profile, "WEBDAV_PASSWORD"));
        return;
      case QuickConnectMode.Sftp:
        EnsureNotEmpty(profile.QuickConnectEndpoint, "SFTP host is required.");
        builder.Append(" --sftp-host ");
        builder.Append(EscapeArgument(profile.QuickConnectEndpoint));
        AppendOptionalPort(builder, "sftp", profile.QuickConnectPort);
        if (!string.IsNullOrWhiteSpace(profile.QuickConnectUsername))
        {
          builder.Append(" --sftp-user ");
          builder.Append(EscapeArgument(profile.QuickConnectUsername));
        }

        builder.Append(" --sftp-pass ");
        builder.Append(BuildScriptPasswordValue(profile, "SFTP_PASSWORD"));
        return;
      case QuickConnectMode.Ftp:
      case QuickConnectMode.Ftps:
        EnsureNotEmpty(profile.QuickConnectEndpoint, "FTP host is required.");
        builder.Append(" --ftp-host ");
        builder.Append(EscapeArgument(profile.QuickConnectEndpoint));
        AppendOptionalPort(builder, "ftp", profile.QuickConnectPort);
        if (!string.IsNullOrWhiteSpace(profile.QuickConnectUsername))
        {
          builder.Append(" --ftp-user ");
          builder.Append(EscapeArgument(profile.QuickConnectUsername));
        }

        if (profile.QuickConnectMode is QuickConnectMode.Ftps)
        {
          builder.Append(" --ftp-tls");
        }

        builder.Append(" --ftp-pass ");
        builder.Append(BuildScriptPasswordValue(profile, "FTP_PASSWORD"));
        return;
    }
  }

  private static string BuildScriptPasswordValue(MountProfile profile, string envVar)
  {
    if (profile.AllowInsecurePasswordsInScript)
    {
      return EscapeArgument(profile.QuickConnectPassword);
    }

    return $"\"${{{envVar}:?set {envVar}}}\"";
  }

  private async Task AddQuickConnectArgumentsAsync(
    MountProfile profile,
    List<string> arguments,
    CancellationToken cancellationToken)
  {
    switch (profile.QuickConnectMode)
    {
      case QuickConnectMode.None:
        return;
      case QuickConnectMode.WebDav:
        EnsureNotEmpty(profile.QuickConnectEndpoint, "WebDAV URL is required.");
        arguments.Add("--webdav-url");
        arguments.Add(profile.QuickConnectEndpoint);
        arguments.Add("--webdav-vendor");
        arguments.Add("other");
        if (!string.IsNullOrWhiteSpace(profile.QuickConnectUsername))
        {
          arguments.Add("--webdav-user");
          arguments.Add(profile.QuickConnectUsername);
        }

        await AddObscuredPasswordArgumentAsync(
          profile,
          arguments,
          "--webdav-pass",
          profile.QuickConnectPassword,
          cancellationToken);
        return;
      case QuickConnectMode.Sftp:
        EnsureNotEmpty(profile.QuickConnectEndpoint, "SFTP host is required.");
        arguments.Add("--sftp-host");
        arguments.Add(profile.QuickConnectEndpoint);
        AddOptionalPort(arguments, "--sftp-port", profile.QuickConnectPort);
        if (!string.IsNullOrWhiteSpace(profile.QuickConnectUsername))
        {
          arguments.Add("--sftp-user");
          arguments.Add(profile.QuickConnectUsername);
        }

        await AddObscuredPasswordArgumentAsync(
          profile,
          arguments,
          "--sftp-pass",
          profile.QuickConnectPassword,
          cancellationToken);
        return;
      case QuickConnectMode.Ftp:
      case QuickConnectMode.Ftps:
        EnsureNotEmpty(profile.QuickConnectEndpoint, "FTP host is required.");
        arguments.Add("--ftp-host");
        arguments.Add(profile.QuickConnectEndpoint);
        AddOptionalPort(arguments, "--ftp-port", profile.QuickConnectPort);
        if (!string.IsNullOrWhiteSpace(profile.QuickConnectUsername))
        {
          arguments.Add("--ftp-user");
          arguments.Add(profile.QuickConnectUsername);
        }

        if (profile.QuickConnectMode is QuickConnectMode.Ftps)
        {
          arguments.Add("--ftp-tls");
        }

        await AddObscuredPasswordArgumentAsync(
          profile,
          arguments,
          "--ftp-pass",
          profile.QuickConnectPassword,
          cancellationToken);
        return;
    }
  }

  private async Task AddObscuredPasswordArgumentAsync(
    MountProfile profile,
    List<string> arguments,
    string argumentName,
    string rawPassword,
    CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(rawPassword))
    {
      return;
    }

    BufferedCommandResult obscureResult = await Cli
      .Wrap(string.IsNullOrWhiteSpace(profile.RcloneBinaryPath) ? "rclone" : profile.RcloneBinaryPath)
      .WithArguments(["obscure", rawPassword])
      .WithValidation(CommandResultValidation.None)
      .ExecuteBufferedAsync(cancellationToken);

    if (obscureResult.ExitCode != 0 || string.IsNullOrWhiteSpace(obscureResult.StandardOutput))
    {
      throw new InvalidOperationException("Could not process password via 'rclone obscure'.");
    }

    arguments.Add(argumentName);
    arguments.Add(obscureResult.StandardOutput.Trim());
  }

  private static void EnsureNotEmpty(string value, string message)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      throw new InvalidOperationException(message);
    }
  }

  private static void AddOptionalPort(List<string> arguments, string argName, string port)
  {
    if (!string.IsNullOrWhiteSpace(port))
    {
      arguments.Add(argName);
      arguments.Add(port.Trim());
    }
  }

  private static void AppendOptionalPort(StringBuilder builder, string protocol, string port)
  {
    if (!string.IsNullOrWhiteSpace(port))
    {
      builder.Append($" --{protocol}-port ");
      builder.Append(EscapeArgument(port.Trim()));
    }
  }

  private static bool IsRcloneMountType(MountType mountType)
  {
    return mountType is MountType.RcloneAuto or MountType.RcloneFuse or MountType.RcloneNfs;
  }

  private async Task<string> ResolveRcloneMountCommandAsync(
    string binary,
    MountType mountType,
    CancellationToken cancellationToken)
  {
    if (mountType is MountType.RcloneFuse)
    {
      _logger.LogInformation("User forced FUSE mount");
      return "mount";
    }

    if (mountType is MountType.RcloneNfs)
    {
      _logger.LogInformation("User forced NFS mount via rclone");
      return "nfsmount";
    }

    if (_rcloneMountCommandCache.TryGetValue(binary, out string? cached))
    {
      return cached;
    }

    bool hasMount = await HasRcloneSubcommandAsync(binary, "mount", cancellationToken);
    bool hasNfsMount = await HasRcloneSubcommandAsync(binary, "nfsmount", cancellationToken);

    if (!hasMount && !hasNfsMount)
    {
      throw new InvalidOperationException("Could not find 'mount' or 'nfsmount' in this rclone build.");
    }

    string selected = hasMount ? "mount" : "nfsmount";

    if (OperatingSystem.IsMacOS())
    {
      string tags = await GetRcloneGoTagsAsync(binary, cancellationToken);
      bool hasCmount = tags.Contains("cmount", StringComparison.OrdinalIgnoreCase);

      selected = hasCmount && hasMount
        ? "mount"
        : hasNfsMount
          ? "nfsmount"
          : "mount";

      string tagText = string.IsNullOrWhiteSpace(tags) ? "none" : tags;
      _logger.LogInformation("Detected rclone tags '{Tags}', using '{MountCommand}'", tagText, selected);
    }
    else
    {
      _logger.LogInformation("Using rclone command '{MountCommand}'", selected);
    }

    _rcloneMountCommandCache[binary] = selected;
    return selected;
  }

  private string GetRcloneMountCommandForScript(string binary, MountType mountType)
  {
    if (_rcloneMountCommandCache.TryGetValue(binary, out string? detected))
    {
      return detected;
    }

    return mountType switch
    {
      MountType.RcloneFuse => "mount",
      MountType.RcloneNfs => "nfsmount",
      _ => OperatingSystem.IsMacOS() ? "nfsmount" : "mount",
    };
  }

  private static async Task<bool> HasRcloneSubcommandAsync(
    string binary,
    string subcommand,
    CancellationToken cancellationToken)
  {
    BufferedCommandResult result = await Cli.Wrap(binary)
      .WithArguments(["help", subcommand])
      .WithValidation(CommandResultValidation.None)
      .ExecuteBufferedAsync(cancellationToken);

    return result.ExitCode == 0;
  }

  private static async Task<string> GetRcloneGoTagsAsync(string binary, CancellationToken cancellationToken)
  {
    BufferedCommandResult result = await Cli.Wrap(binary)
      .WithArguments(["version"])
      .WithValidation(CommandResultValidation.None)
      .ExecuteBufferedAsync(cancellationToken);

    if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
    {
      return string.Empty;
    }

    string[] lines = result.StandardOutput.Split(
      '\n',
      StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    foreach (string line in lines)
    {
      string marker = "go/tags:";
      int index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
      if (index < 0)
      {
        continue;
      }

      return line[(index + marker.Length)..].Trim();
    }

    return string.Empty;
  }

  /// <summary>
  /// Classifies an rclone stderr line by its log level prefix and returns an
  /// appropriately tagged string for downstream severity resolution.
  /// rclone writes NOTICE, WARNING, ERROR, and CRITICAL to stderr.
  /// </summary>
  internal static string ClassifyRcloneStderrLine(string line)
  {
    if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("CRITICAL", StringComparison.OrdinalIgnoreCase))
    {
      return $"ERR: {line}";
    }

    if (line.Contains("WARNING", StringComparison.OrdinalIgnoreCase))
    {
      return $"WARN: {line}";
    }

    return line;
  }

  private void LogClassifiedStderrLine(string line)
  {
    if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("CRITICAL", StringComparison.OrdinalIgnoreCase))
    {
      _logger.LogError("{Line}", line);
    }
    else if (line.Contains("WARNING", StringComparison.OrdinalIgnoreCase))
    {
      _logger.LogWarning("{Line}", line);
    }
    else
    {
      _logger.LogInformation("{Line}", line);
    }
  }

  public static int AssignRcPort(string profileId)
  {
    int hash = profileId.GetHashCode(StringComparison.OrdinalIgnoreCase);
    return 50000 + Math.Abs(hash) % 10000;
  }

  public void AdoptMount(string mountPoint, int pid, int rcPort)
  {
    string resolved = ResolveMountPoint(mountPoint);
    _runningMounts.TryAdd(resolved, new RunningMount(pid, rcPort));
  }

  public async Task StopViaRcAsync(int rcPort, CancellationToken cancellationToken)
  {
    RcloneRcClient rcClient = new(new HttpClient());
    await rcClient.QuitAsync(rcPort, cancellationToken);
  }

  public async Task<int?> ProbeRcPidAsync(int rcPort, CancellationToken cancellationToken)
  {
    if (rcPort <= 0)
    {
      return null;
    }

    RcloneRcClient rcClient = new(new HttpClient());
    return await rcClient.GetPidAsync(rcPort, cancellationToken);
  }

  public static string ResolveAbsoluteBinaryPath(string binaryPath)
  {
    if (string.IsNullOrWhiteSpace(binaryPath))
    {
      binaryPath = "rclone";
    }

    if (Path.IsPathRooted(binaryPath))
    {
      return binaryPath;
    }

    string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
    char separator = OperatingSystem.IsWindows() ? ';' : ':';

    foreach (string dir in pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries))
    {
      string candidate = Path.Combine(dir, binaryPath);
      if (File.Exists(candidate))
      {
        return candidate;
      }
    }

    return binaryPath;
  }

  private static string ReadLogTail(string logFile, int maxLines = 20)
  {
    try
    {
      if (!File.Exists(logFile))
      {
        return string.Empty;
      }

      string[] lines = File.ReadAllLines(logFile);
      int start = Math.Max(0, lines.Length - maxLines);
      return string.Join('\n', lines[start..]);
    }
    catch
    {
      return string.Empty;
    }
  }

  public static string ExtractRcloneErrorDetail(string logTail)
  {
    if (string.IsNullOrWhiteSpace(logTail))
    {
      return string.Empty;
    }

    List<string> errorLines = new();
    foreach (string line in logTail.Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
      if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
          line.Contains("CRITICAL", StringComparison.OrdinalIgnoreCase) ||
          line.Contains("Fatal", StringComparison.OrdinalIgnoreCase))
      {
        errorLines.Add(line.Trim());
      }
    }

    return errorLines.Count > 0
      ? string.Join(Environment.NewLine, errorLines)
      : string.Empty;
  }

  private sealed record RunningMount(int Pid, int RcPort);
}