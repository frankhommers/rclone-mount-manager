using CliWrap;
using CliWrap.Buffered;
using RcloneMountManager.Core.Models;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RcloneMountManager.Core.Services;

public sealed class LaunchAgentService
{
    private readonly string _appDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RcloneMountManager");

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
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "LaunchAgents");

        return Path.Combine(launchAgentsDirectory, $"com.rclonemountmanager.profile.{profile.Id}.plist");
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

        await RunLaunchCtlAsync(["unload", "-w", plistPath], cancellationToken);
        await RunLaunchCtlAsync(["load", "-w", plistPath], cancellationToken);

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
            await RunLaunchCtlAsync(["unload", "-w", plistPath], cancellationToken);
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
        var label = $"com.rclonemountmanager.profile.{profile.Id}";
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

    private static async Task RunLaunchCtlAsync(string[] args, CancellationToken cancellationToken)
    {
        await Cli.Wrap("launchctl")
            .WithArguments(args)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);
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
