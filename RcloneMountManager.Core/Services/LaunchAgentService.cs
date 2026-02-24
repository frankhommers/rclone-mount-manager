using CliWrap;
using CliWrap.Buffered;
using RcloneMountManager.Core.Models;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RcloneMountManager.Core.Services;

public sealed class LaunchAgentService
{
    private readonly string _appDataDirectory;
    private readonly string _userProfileDirectory;
    private readonly Func<string, string[], CancellationToken, Task<CommandExecutionResult>> _commandRunner;
    private readonly Func<uint> _uidProvider;

    [DllImport("libc")]
    private static extern uint getuid();

    public readonly record struct CommandExecutionResult(int ExitCode, string StandardOutput, string StandardError);

    public LaunchAgentService(
        string? appDataDirectory = null,
        string? userProfileDirectory = null,
        Func<string, string[], CancellationToken, Task<CommandExecutionResult>>? commandRunner = null,
        Func<uint>? uidProvider = null)
    {
        _appDataDirectory = appDataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RcloneMountManager");
        _userProfileDirectory = userProfileDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _commandRunner = commandRunner ?? ExecuteCommandAsync;
        _uidProvider = uidProvider ?? getuid;
    }

    public bool IsSupported => OperatingSystem.IsMacOS();

    public string GetScriptPath(MountProfile profile)
    {
        var scriptsDirectory = Path.Combine(_appDataDirectory, "scripts");
        var safeName = BuildSafeFileName(profile.Name);
        return Path.Combine(scriptsDirectory, $"{safeName}-{profile.Id}.sh");
    }

    public string GetLaunchAgentPlistPath(MountProfile profile)
    {
        var launchAgentsDirectory = Path.Combine(
            _userProfileDirectory,
            "Library",
            "LaunchAgents");

        return Path.Combine(launchAgentsDirectory, $"{BuildLabel(profile)}.plist");
    }

    public async Task EnableAsync(MountProfile profile, string scriptContent, Action<string> log, CancellationToken cancellationToken)
    {
        if (!IsSupported)
        {
            throw new InvalidOperationException("Start at login is currently implemented for macOS only.");
        }

        var scriptPath = GetScriptPath(profile);
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        await File.WriteAllTextAsync(scriptPath, scriptContent, cancellationToken);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(scriptPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        var plistPath = GetLaunchAgentPlistPath(profile);
        Directory.CreateDirectory(Path.GetDirectoryName(plistPath)!);
        var plistContent = BuildPlist(profile, scriptPath);
        await File.WriteAllTextAsync(plistPath, plistContent, cancellationToken);

        await RunPlutilLintAsync(plistPath, cancellationToken);

        var bootoutResult = await _commandRunner("launchctl", ["bootout", BuildServiceTarget(profile)], cancellationToken);
        if (bootoutResult.ExitCode == 0)
        {
            log("Removed stale LaunchAgent before re-registering.");
        }

        await RunLaunchCtlAsync(["bootstrap", BuildGuiDomain(), plistPath], cancellationToken);

        log($"Enabled start at login for '{profile.Name}'.");
        log($"LaunchAgent: {plistPath}");
    }

    public async Task DisableAsync(MountProfile profile, Action<string> log, CancellationToken cancellationToken)
    {
        if (!IsSupported)
        {
            return;
        }

        var plistPath = GetLaunchAgentPlistPath(profile);
        if (File.Exists(plistPath))
        {
            var result = await _commandRunner("launchctl", ["bootout", BuildServiceTarget(profile)], cancellationToken);
            if (result.ExitCode != 0)
            {
                log($"launchctl bootout exited with code {result.ExitCode} (service may already be unloaded).");
            }

            File.Delete(plistPath);
        }

        log($"Disabled start at login for '{profile.Name}'.");
    }

    public bool IsEnabled(MountProfile profile)
    {
        return File.Exists(GetLaunchAgentPlistPath(profile));
    }

    private static string BuildPlist(MountProfile profile, string scriptPath)
    {
        var label = BuildLabel(profile);
        var escapedPath = EscapeXml(scriptPath);
        var escapedLabel = EscapeXml(label);

        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.AppendLine("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");
        builder.AppendLine("<plist version=\"1.0\">");
        builder.AppendLine("<dict>");
        builder.AppendLine("  <key>Label</key>");
        builder.AppendLine($"  <string>{escapedLabel}</string>");
        builder.AppendLine("  <key>ProgramArguments</key>");
        builder.AppendLine("  <array>");
        builder.AppendLine("    <string>/bin/bash</string>");
        builder.AppendLine($"    <string>{escapedPath}</string>");
        builder.AppendLine("  </array>");
        builder.AppendLine("  <key>RunAtLoad</key>");
        builder.AppendLine("  <true/>");
        builder.AppendLine("  <key>KeepAlive</key>");
        builder.AppendLine("  <false/>");
        builder.AppendLine("</dict>");
        builder.AppendLine("</plist>");
        return builder.ToString();
    }

    private async Task RunLaunchCtlAsync(string[] args, CancellationToken cancellationToken)
    {
        await RunCommandWithStrictValidationAsync("launchctl", args, cancellationToken);
    }

    private async Task RunPlutilLintAsync(string plistPath, CancellationToken cancellationToken)
    {
        await RunCommandWithStrictValidationAsync("plutil", ["-lint", plistPath], cancellationToken);
    }

    private async Task RunCommandWithStrictValidationAsync(string command, string[] args, CancellationToken cancellationToken)
    {
        var result = await _commandRunner(command, args, cancellationToken);

        if (result.ExitCode != 0)
        {
            var stderr = string.IsNullOrWhiteSpace(result.StandardError)
                ? "(empty)"
                : result.StandardError.Trim();
            var stdout = string.IsNullOrWhiteSpace(result.StandardOutput)
                ? "(empty)"
                : result.StandardOutput.Trim();

            throw new InvalidOperationException(
                $"Command '{command} {string.Join(' ', args)}' failed with exit code {result.ExitCode}. stdout: {stdout} stderr: {stderr}");
        }
    }

    private static string BuildLabel(MountProfile profile)
    {
        return $"com.rclonemountmanager.profile.{profile.Id}";
    }

    private string BuildGuiDomain()
    {
        return $"gui/{_uidProvider()}";
    }

    private string BuildServiceTarget(MountProfile profile)
    {
        return $"{BuildGuiDomain()}/{BuildLabel(profile)}";
    }

    private static async Task<CommandExecutionResult> ExecuteCommandAsync(string command, string[] args, CancellationToken cancellationToken)
    {
        var result = await Cli.Wrap(command)
            .WithArguments(args)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);

        return new CommandExecutionResult(result.ExitCode, result.StandardOutput, result.StandardError);
    }

    private static string BuildSafeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(fileName.Length);

        foreach (var ch in fileName)
        {
            builder.Append(invalidChars.Contains(ch) ? '-' : ch);
        }

        return builder.Length == 0 ? "mount-profile" : builder.ToString();
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
    }
}
