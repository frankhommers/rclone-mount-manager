using CliWrap;
using CliWrap.Buffered;
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
    private readonly ConcurrentDictionary<string, RunningMount> _runningMounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _rcloneMountCommandCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task StartAsync(MountProfile profile, Action<string> log, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var mountPoint = ResolveMountPoint(profile.MountPoint);

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
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var suggestion = Path.Combine(home, "Mounts", "my-mount");
            throw new InvalidOperationException(
                $"No write access to mount path '{profile.MountPoint}'. Choose a folder inside your home directory, for example '{suggestion}'.",
                ex);
        }

        if (!string.Equals(profile.MountPoint, mountPoint, StringComparison.Ordinal))
        {
            log($"Resolved mount path '{profile.MountPoint}' -> '{mountPoint}'.");
        }

        if (IsRcloneMountType(profile.Type))
        {
            await StartRcloneAsync(profile, mountPoint, log, cancellationToken);
            return;
        }

        await StartNfsAsync(profile, mountPoint, log, cancellationToken);
    }

    public async Task StopAsync(MountProfile profile, Action<string> log, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var mountPoint = ResolveMountPoint(profile.MountPoint);

        if (IsRcloneMountType(profile.Type) && _runningMounts.TryRemove(mountPoint, out var runningMount))
        {
            if (runningMount.RcPort > 0)
            {
                log("Sending quit via RC...");
                var rcClient = new RcloneRcClient(new HttpClient());
                await rcClient.QuitAsync(runningMount.RcPort, cancellationToken);

                for (int attempt = 0; attempt < 10; attempt++)
                {
                    await Task.Delay(500, cancellationToken);
                    if (!await IsMountedAsync(mountPoint, cancellationToken))
                    {
                        log("rclone stopped via RC.");
                        return;
                    }
                }

                log("RC quit timed out, falling back to umount.");
            }
            else
            {
                log("No RC port, falling back to umount.");
            }
        }
        else if (IsRcloneMountType(profile.Type))
        {
            if (profile.EnableRemoteControl && profile.RcPort > 0)
            {
                log("No tracked process; trying RC quit for orphan...");
                var rcClient = new RcloneRcClient(new HttpClient());
                if (await rcClient.QuitAsync(profile.RcPort, cancellationToken))
                {
                    for (int attempt = 0; attempt < 10; attempt++)
                    {
                        await Task.Delay(500, cancellationToken);
                        if (!await IsMountedAsync(mountPoint, cancellationToken))
                        {
                            log("Orphan rclone stopped via RC.");
                            return;
                        }
                    }
                }
            }

            log("No tracked rclone process found; attempting unmount of orphan mount.");
        }

        await UnmountAsync(mountPoint, log, cancellationToken);
    }

    public async Task<bool> IsMountedAsync(string mountPoint, CancellationToken cancellationToken)
    {
        mountPoint = ResolveMountPoint(mountPoint);

        var result = await Cli.Wrap("mount")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);

        if (result.ExitCode != 0)
        {
            return false;
        }

        var target = $" on {mountPoint}";
        return result.StandardOutput.Contains(target, StringComparison.Ordinal);
    }

    public bool IsRunning(string mountPoint) => _runningMounts.ContainsKey(ResolveMountPoint(mountPoint));

    public async Task TestConnectionAsync(MountProfile profile, Action<string> log, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Type is MountType.MacOsNfs)
        {
            throw new InvalidOperationException("Connectivity test currently supports rclone profiles only.");
        }

        var binary = string.IsNullOrWhiteSpace(profile.RcloneBinaryPath) ? "rclone" : profile.RcloneBinaryPath;

        List<string> arguments;
        if (profile.QuickConnectMode is QuickConnectMode.None)
        {
            var target = BuildConnectivityTarget(profile.Source);
            arguments = new List<string> { "lsd", target, "--max-depth", "1", "-vv" };
        }
        else
        {
            var source = ResolveRcloneSource(profile);
            arguments = new List<string> { "lsd", source, "--max-depth", "1", "-vv" };
            await AddQuickConnectArgumentsAsync(profile, arguments, cancellationToken);
        }

        var result = await Cli.Wrap(binary)
            .WithArguments(arguments)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            log(result.StandardOutput.Trim());
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            foreach (var line in result.StandardError.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;
                log(ClassifyRcloneStderrLine(trimmed));
            }
        }

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Connectivity test failed with exit code {result.ExitCode}.");
        }

        log("Connectivity test succeeded.");
    }

    public async Task TestBackendConnectionAsync(
        string rcloneBinaryPath,
        string backendName,
        IEnumerable<RcloneBackendOptionInput> options,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var binary = string.IsNullOrWhiteSpace(rcloneBinaryPath) ? "rclone" : rcloneBinaryPath;
        var arguments = new List<string> { "lsd", $":{backendName}:", "--max-depth", "1", "-vv" };

        var secretFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var option in options)
        {
            if (string.IsNullOrWhiteSpace(option.Name) || string.IsNullOrWhiteSpace(option.Value))
                continue;

            var flagName = option.Name.Replace('_', '-');
            var flag = $"--{backendName}-{flagName}";

            if (option.ControlType == OptionControlType.Toggle)
            {
                if (string.Equals(option.Value, "true", StringComparison.OrdinalIgnoreCase))
                    arguments.Add(flag);
                continue;
            }

            var flagValue = option.Value;
            if (option.IsPassword)
            {
                secretFlags.Add(flag);
                var obscureResult = await Cli.Wrap(binary)
                    .WithArguments(["obscure", option.Value])
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteBufferedAsync(cancellationToken);

                if (obscureResult.ExitCode != 0)
                    throw new InvalidOperationException($"Failed to obscure password for '{option.Name}'.");

                flagValue = obscureResult.StandardOutput.Trim();
            }

            arguments.Add(flag);
            arguments.Add(flagValue);
        }

        var secretValues = CollectSecretValues(arguments, secretFlags);

        log($"$ {binary} {FormatCommandLine(arguments, secretFlags)}");

        var result = await Cli.Wrap(binary)
            .WithArguments(arguments)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            log(RedactSecrets(result.StandardOutput.Trim(), secretValues));

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            foreach (var line in result.StandardError.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;
                var redacted = RedactSecrets(trimmed, secretValues);
                log(ClassifyRcloneStderrLine(redacted));
            }
        }

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Connectivity test failed with exit code {result.ExitCode}.");

        log("Connectivity test succeeded.");
    }

    public string GenerateScript(MountProfile profile)
    {
        var builder = new StringBuilder();
        builder.AppendLine("#!/usr/bin/env bash");
        builder.AppendLine("set -euo pipefail");
        builder.AppendLine();
        builder.AppendLine($"MOUNT_POINT=\"{EscapeForBash(ResolveMountPointForScript(profile.MountPoint))}\"");
        builder.AppendLine();
        builder.AppendLine("mkdir -p \"$MOUNT_POINT\"");

        if (IsRcloneMountType(profile.Type))
        {
            var binary = string.IsNullOrWhiteSpace(profile.RcloneBinaryPath) ? "rclone" : profile.RcloneBinaryPath;
            var mountCommand = GetRcloneMountCommandForScript(binary, profile.Type);
            var source = ResolveRcloneSource(profile);

            builder.Append("\"");
            builder.Append(EscapeForBash(binary));
            builder.Append("\" ");
            builder.Append(mountCommand);
            builder.Append(' ');
            builder.Append(EscapeArgument(source));
            builder.Append(" \"$MOUNT_POINT\"");
            AppendQuickConnectScriptArgs(profile, builder);

            var options = ParseArguments(profile.ExtraOptions);
            foreach (var option in options)
            {
                builder.Append(' ');
                builder.Append(EscapeArgument(option));
            }

            foreach (var kvp in profile.MountOptions)
            {
                var flag = "--" + kvp.Key.Replace('_', '-');
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
                var hasRcAddr = profile.MountOptions.ContainsKey("rc_addr") ||
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

    private async Task StartRcloneAsync(MountProfile profile, string mountPoint, Action<string> log, CancellationToken cancellationToken)
    {
        if (_runningMounts.ContainsKey(mountPoint))
        {
            throw new InvalidOperationException("rclone mount is already running for this mount point.");
        }

        if (await IsMountedAsync(mountPoint, cancellationToken))
        {
            if (_runningMounts.ContainsKey(mountPoint))
            {
                log("Mount point is already active and tracked.");
                return;
            }

            throw new InvalidOperationException(
                $"Mount point '{mountPoint}' is already in use (possibly from a previous session). Stop the existing mount first.");
        }

        var source = ResolveRcloneSource(profile);
        var binary = string.IsNullOrWhiteSpace(profile.RcloneBinaryPath) ? "rclone" : profile.RcloneBinaryPath;
        var mountCommand = await ResolveRcloneMountCommandAsync(binary, profile.Type, log, cancellationToken);
        var arguments = new List<string> { mountCommand, source, mountPoint };
        await AddQuickConnectArgumentsAsync(profile, arguments, cancellationToken);

        arguments.AddRange(ParseArguments(profile.ExtraOptions));

        foreach (var kvp in profile.MountOptions)
        {
            var flag = "--" + kvp.Key.Replace('_', '-');
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

        var rcEnabled = profile.EnableRemoteControl && profile.RcPort > 0;
        if (rcEnabled)
        {
            var hasRcAddr = arguments.Any(a => a.StartsWith("--rc-addr", StringComparison.OrdinalIgnoreCase));
            if (!hasRcAddr)
            {
                arguments.Add("--rc");
                arguments.Add("--rc-no-auth");
                arguments.Add("--rc-addr");
                arguments.Add($"localhost:{profile.RcPort}");
            }
        }

        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RcloneMountManager", "logs");
        Directory.CreateDirectory(logDir);
        var logFile = Path.Combine(logDir, $"{profile.Id}.log");

        var rcloneArgs = string.Join(" ", arguments.Select(EscapeArgument));
        var shellCommand = $"nohup \"{EscapeForBash(binary)}\" {rcloneArgs} >> \"{EscapeForBash(logFile)}\" 2>&1 &";

        log($"Launching detached: {binary} {mountCommand} {source} {mountPoint}");

        var result = await Cli.Wrap("/bin/sh")
            .WithArguments(["-c", shellCommand])
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to launch rclone: {result.StandardError}");
        }

        if (rcEnabled)
        {
            var rcClient = new RcloneRcClient(new HttpClient());
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

            if (pid.HasValue)
            {
                _runningMounts.TryAdd(mountPoint, new RunningMount(pid.Value, profile.RcPort));
                log($"rclone {mountCommand} started (PID {pid.Value}, RC port {profile.RcPort}).");
            }
            else
            {
                log($"WARN: rclone launched but RC not responding on port {profile.RcPort}. Check log: {logFile}");
            }
        }
        else
        {
            await Task.Delay(2000, cancellationToken);
            if (await IsMountedAsync(mountPoint, cancellationToken))
            {
                log($"rclone {mountCommand} started (no RC, mount point verified).");
                _runningMounts.TryAdd(mountPoint, new RunningMount(0, 0));
            }
            else
            {
                throw new InvalidOperationException($"Mount did not appear after launch. Check the log file: {logFile}");
            }
        }
    }

    private async Task StartNfsAsync(MountProfile profile, string mountPoint, Action<string> log, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(profile.ExtraOptions) && profile.ExtraOptions.Contains("--", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("NFS options look like rclone flags (contain '--'). Use NFS options such as 'nfsvers=4,resvport' or switch type to Rclone.");
        }

        var arguments = new List<string> { "-t", "nfs" };

        if (!string.IsNullOrWhiteSpace(profile.ExtraOptions))
        {
            arguments.Add("-o");
            arguments.Add(profile.ExtraOptions);
        }

        arguments.Add(profile.Source);
        arguments.Add(mountPoint);

        var result = await Cli.Wrap("mount")
            .WithArguments(arguments)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            log(result.StandardOutput.Trim());
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            log($"ERR: {result.StandardError.Trim()}");
        }

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"NFS mount failed with exit code {result.ExitCode}.");
        }
    }

    private async Task UnmountAsync(string mountPoint, Action<string> log, CancellationToken cancellationToken)
    {
        if (!await IsMountedAsync(mountPoint, cancellationToken))
        {
            log("Mount point is not mounted.");
            return;
        }

        var unmountCandidates = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? new[]
            {
                (Binary: "fusermount", Args: new[] { "-u", mountPoint }),
                (Binary: "umount", Args: new[] { mountPoint }),
            }
            : new[]
            {
                (Binary: "umount", Args: new[] { mountPoint }),
            };

        foreach (var candidate in unmountCandidates)
        {
            var result = await Cli.Wrap(candidate.Binary)
                .WithArguments(candidate.Args)
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(cancellationToken);

            if (result.ExitCode == 0)
            {
                log($"Unmounted {mountPoint} via {candidate.Binary}.");
                return;
            }
        }

        throw new InvalidOperationException($"Could not unmount {mountPoint}. Try unmounting manually.");
    }

    private static string ResolveMountPoint(string mountPoint)
    {
        if (string.IsNullOrWhiteSpace(mountPoint))
        {
            return mountPoint;
        }

        var trimmed = mountPoint.Trim();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

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

        var trimmed = mountPoint.Trim();
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

        var trimmed = source.Trim();
        var colonIndex = trimmed.IndexOf(':');
        if (colonIndex <= 0)
        {
            return trimmed;
        }

        return trimmed[..(colonIndex + 1)];
    }

    private static IReadOnlyList<string> ParseArguments(string input)
    {
        var args = new List<string>();
        if (string.IsNullOrWhiteSpace(input))
        {
            return args;
        }

        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in input)
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
        var parts = new List<string>();
        for (var i = 0; i < arguments.Count; i++)
        {
            var arg = arguments[i];
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
        var values = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < arguments.Count; i++)
        {
            if (secretFlags.Contains(arguments[i]) && i + 1 < arguments.Count)
            {
                var secretValue = arguments[i + 1];
                if (!string.IsNullOrWhiteSpace(secretValue))
                    values.Add(secretValue);
                i++;
            }
        }

        return values;
    }

    private static string RedactSecrets(string text, HashSet<string> secretValues)
    {
        foreach (var secret in secretValues)
        {
            text = text.Replace(secret, "****", StringComparison.Ordinal);
        }

        return text;
    }

    private static string EscapeForBash(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string EscapeArgument(string value)
    {
        var escaped = value.Replace("'", "'\"'\"'");
        return $"'{escaped}'";
    }

    private static string ResolveRcloneSource(MountProfile profile)
    {
        if (profile.QuickConnectMode is not QuickConnectMode.None)
        {
            var subPath = string.IsNullOrWhiteSpace(profile.Source) ? "/" : profile.Source.Trim();
            if (!subPath.StartsWith("/", StringComparison.Ordinal))
            {
                subPath = "/" + subPath;
            }

            var backend = profile.QuickConnectMode switch
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

    private async Task AddQuickConnectArgumentsAsync(MountProfile profile, List<string> arguments, CancellationToken cancellationToken)
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

                await AddObscuredPasswordArgumentAsync(profile, arguments, "--webdav-pass", profile.QuickConnectPassword, cancellationToken);
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

                await AddObscuredPasswordArgumentAsync(profile, arguments, "--sftp-pass", profile.QuickConnectPassword, cancellationToken);
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

                await AddObscuredPasswordArgumentAsync(profile, arguments, "--ftp-pass", profile.QuickConnectPassword, cancellationToken);
                return;
        }
    }

    private async Task AddObscuredPasswordArgumentAsync(MountProfile profile, List<string> arguments, string argumentName, string rawPassword, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawPassword))
        {
            return;
        }

        var obscureResult = await Cli.Wrap(string.IsNullOrWhiteSpace(profile.RcloneBinaryPath) ? "rclone" : profile.RcloneBinaryPath)
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

    private static bool IsRcloneMountType(MountType mountType) =>
        mountType is MountType.RcloneAuto or MountType.RcloneFuse or MountType.RcloneNfs;

    private async Task<string> ResolveRcloneMountCommandAsync(string binary, MountType mountType, Action<string> log, CancellationToken cancellationToken)
    {
        if (mountType is MountType.RcloneFuse)
        {
            log("User forced FUSE mount.");
            return "mount";
        }

        if (mountType is MountType.RcloneNfs)
        {
            log("User forced NFS mount via rclone.");
            return "nfsmount";
        }

        if (_rcloneMountCommandCache.TryGetValue(binary, out var cached))
        {
            return cached;
        }

        var hasMount = await HasRcloneSubcommandAsync(binary, "mount", cancellationToken);
        var hasNfsMount = await HasRcloneSubcommandAsync(binary, "nfsmount", cancellationToken);

        if (!hasMount && !hasNfsMount)
        {
            throw new InvalidOperationException("Could not find 'mount' or 'nfsmount' in this rclone build.");
        }

        var selected = hasMount ? "mount" : "nfsmount";

        if (OperatingSystem.IsMacOS())
        {
            var tags = await GetRcloneGoTagsAsync(binary, cancellationToken);
            var hasCmount = tags.Contains("cmount", StringComparison.OrdinalIgnoreCase);

            selected = hasCmount && hasMount
                ? "mount"
                : hasNfsMount
                    ? "nfsmount"
                    : "mount";

            var tagText = string.IsNullOrWhiteSpace(tags) ? "none" : tags;
            log($"Detected rclone tags '{tagText}', using '{selected}'.");
        }
        else
        {
            log($"Using rclone command '{selected}'.");
        }

        _rcloneMountCommandCache[binary] = selected;
        return selected;
    }

    private string GetRcloneMountCommandForScript(string binary, MountType mountType)
    {
        if (_rcloneMountCommandCache.TryGetValue(binary, out var detected))
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

    private static async Task<bool> HasRcloneSubcommandAsync(string binary, string subcommand, CancellationToken cancellationToken)
    {
        var result = await Cli.Wrap(binary)
            .WithArguments(["help", subcommand])
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);

        return result.ExitCode == 0;
    }

    private static async Task<string> GetRcloneGoTagsAsync(string binary, CancellationToken cancellationToken)
    {
        var result = await Cli.Wrap(binary)
            .WithArguments(["version"])
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return string.Empty;
        }

        var lines = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            var marker = "go/tags:";
            var index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
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

    public static int AssignRcPort(string profileId)
    {
        var hash = profileId.GetHashCode(StringComparison.OrdinalIgnoreCase);
        return 50000 + (Math.Abs(hash) % 10000);
    }

    public void AdoptMount(string mountPoint, int pid, int rcPort)
    {
        var resolved = ResolveMountPoint(mountPoint);
        _runningMounts.TryAdd(resolved, new RunningMount(pid, rcPort));
    }

    public async Task StopViaRcAsync(int rcPort, CancellationToken cancellationToken)
    {
        var rcClient = new RcloneRcClient(new HttpClient());
        await rcClient.QuitAsync(rcPort, cancellationToken);
    }

    public async Task<int?> ProbeRcPidAsync(int rcPort, CancellationToken cancellationToken)
    {
        if (rcPort <= 0) return null;
        var rcClient = new RcloneRcClient(new HttpClient());
        return await rcClient.GetPidAsync(rcPort, cancellationToken);
    }

    private sealed record RunningMount(int Pid, int RcPort);
}
