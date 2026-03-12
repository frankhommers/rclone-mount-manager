using CliWrap;
using CliWrap.Buffered;
using Microsoft.Extensions.Logging;
using RcloneMountManager.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RcloneMountManager.Core.Services;

public sealed class StartupPreflightService
{
  public const string BinaryCheckKey = "binary";
  public const string MountPathCheckKey = "mount-path";
  public const string CachePathCheckKey = "cache-path";
  public const string CredentialsCheckKey = "credentials";
  public const string SourceRemoteCheckKey = "source-remote";

  private readonly ILogger<StartupPreflightService> _logger;

  public StartupPreflightService(ILogger<StartupPreflightService> logger)
  {
    _logger = logger;
  }

  public async Task<StartupPreflightReport> RunAsync(MountProfile profile, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(profile);

    StartupPreflightReport report = new();

    cancellationToken.ThrowIfCancellationRequested();
    RunBinaryCheck(profile, report);

    cancellationToken.ThrowIfCancellationRequested();
    RunMountPathCheck(profile, report);

    cancellationToken.ThrowIfCancellationRequested();
    RunCachePathCheck(profile, report);

    cancellationToken.ThrowIfCancellationRequested();
    RunCredentialsCheck(profile, report);

    cancellationToken.ThrowIfCancellationRequested();
    await RunSourceRemoteCheckAsync(profile, report, cancellationToken);

    return report;
  }

  private static void RunBinaryCheck(MountProfile profile, StartupPreflightReport report)
  {
    if (!IsRcloneMountType(profile.Type))
    {
      report.AddPass(BinaryCheckKey, "rclone binary check skipped for non-rclone profile.");
      return;
    }

    string binary = string.IsNullOrWhiteSpace(profile.RcloneBinaryPath) ? "rclone" : profile.RcloneBinaryPath.Trim();
    if (!TryResolveBinaryPath(binary, out string resolvedPath, out string error))
    {
      report.AddCritical(BinaryCheckKey, $"Could not resolve executable rclone binary '{binary}': {error}");
      return;
    }

    report.AddPass(BinaryCheckKey, $"Using rclone binary '{resolvedPath}'.");
  }

  private static void RunMountPathCheck(MountProfile profile, StartupPreflightReport report)
  {
    if (string.IsNullOrWhiteSpace(profile.MountPoint))
    {
      report.AddCritical(MountPathCheckKey, "Mount path is required.");
      return;
    }

    if (!TryResolvePath(profile.MountPoint, out string mountPath, out string resolveError))
    {
      report.AddCritical(MountPathCheckKey, $"Mount path '{profile.MountPoint}' is invalid: {resolveError}");
      return;
    }

    string? parentDir = Path.GetDirectoryName(mountPath);
    if (!string.IsNullOrWhiteSpace(parentDir) && !Directory.Exists(parentDir))
    {
      report.AddCritical(MountPathCheckKey, $"Parent directory '{parentDir}' does not exist.");
      return;
    }

    if (Directory.Exists(mountPath))
    {
      report.AddPass(MountPathCheckKey, $"Mount path exists: '{mountPath}'.");
      return;
    }

    if (!TryEnsureWritableDirectory(mountPath, out string error))
    {
      report.AddCritical(MountPathCheckKey, $"Mount path '{profile.MountPoint}' is not writable: {error}");
      return;
    }

    report.AddPass(MountPathCheckKey, $"Mount path is writable: '{mountPath}'.");
  }

  private static void RunCachePathCheck(MountProfile profile, StartupPreflightReport report)
  {
    if (!TryGetConfiguredCachePath(profile, out string? configuredCachePath, out string extractionError))
    {
      report.AddCritical(CachePathCheckKey, extractionError);
      return;
    }

    if (string.IsNullOrWhiteSpace(configuredCachePath))
    {
      report.AddWarning(CachePathCheckKey, "No explicit cache path configured.");
      return;
    }

    if (!TryResolvePath(configuredCachePath, out string cachePath, out string resolveError))
    {
      report.AddCritical(CachePathCheckKey, $"Cache path '{configuredCachePath}' is invalid: {resolveError}");
      return;
    }

    if (!TryEnsureWritableDirectory(cachePath, out string error))
    {
      report.AddCritical(CachePathCheckKey, $"Cache path '{configuredCachePath}' is not writable: {error}");
      return;
    }

    report.AddPass(CachePathCheckKey, $"Cache path is writable: '{cachePath}'.");
  }

  private static void RunCredentialsCheck(MountProfile profile, StartupPreflightReport report)
  {
    if (profile.QuickConnectMode is QuickConnectMode.None)
    {
      report.AddPass(CredentialsCheckKey, "Quick connect credentials are not required for this profile.");
      return;
    }

    string modeName = profile.QuickConnectMode.ToString();
    if (string.IsNullOrWhiteSpace(profile.QuickConnectEndpoint))
    {
      report.AddCritical(CredentialsCheckKey, $"{modeName} endpoint is required.");
      return;
    }

    if (string.IsNullOrWhiteSpace(profile.QuickConnectPassword))
    {
      report.AddCritical(CredentialsCheckKey, $"{modeName} credentials are unavailable: password is required.");
      return;
    }

    report.AddPass(CredentialsCheckKey, $"{modeName} credentials are available.");
  }

  private async Task RunSourceRemoteCheckAsync(
    MountProfile profile,
    StartupPreflightReport report,
    CancellationToken cancellationToken)
  {
    if (!IsRcloneMountType(profile.Type))
    {
      report.AddPass(SourceRemoteCheckKey, "Source remote check skipped for non-rclone profile.");
      return;
    }

    if (profile.QuickConnectMode is not QuickConnectMode.None)
    {
      report.AddPass(SourceRemoteCheckKey, "Source remote check skipped for quick connect profile.");
      return;
    }

    if (!TryExtractRemoteAlias(profile.Source, out string remoteAlias))
    {
      report.AddCritical(SourceRemoteCheckKey, $"Could not extract remote name from source '{profile.Source}'.");
      return;
    }

    string binary = string.IsNullOrWhiteSpace(profile.RcloneBinaryPath) ? "rclone" : profile.RcloneBinaryPath.Trim();

    try
    {
      BufferedCommandResult result = await Cli.Wrap(binary)
        .WithArguments(["listremotes"])
        .WithValidation(CommandResultValidation.None)
        .ExecuteBufferedAsync(cancellationToken);

      if (result.ExitCode != 0)
      {
        report.AddWarning(SourceRemoteCheckKey, $"Could not list rclone remotes (exit code {result.ExitCode}).");
        return;
      }

      HashSet<string> remotes = result.StandardOutput
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(r => r.TrimEnd(':'))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

      if (remotes.Contains(remoteAlias))
      {
        report.AddPass(SourceRemoteCheckKey, $"Source remote '{remoteAlias}' exists in rclone config.");
        return;
      }

      report.AddCritical(
        SourceRemoteCheckKey,
        $"Source remote '{remoteAlias}' is not defined in rclone config. " +
        "Configure it via the remote editor or run 'rclone config create'.");
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (Exception ex)
    {
      report.AddWarning(SourceRemoteCheckKey, $"Could not verify source remote: {ex.Message}");
    }
  }

  public static bool TryExtractRemoteAlias(string source, out string remoteAlias)
  {
    remoteAlias = string.Empty;

    if (string.IsNullOrWhiteSpace(source))
    {
      return false;
    }

    string trimmed = source.Trim();
    if (trimmed.StartsWith(":", StringComparison.Ordinal))
    {
      return false;
    }

    int colonIndex = trimmed.IndexOf(':');
    if (colonIndex <= 0)
    {
      return false;
    }

    remoteAlias = trimmed[..colonIndex];
    return true;
  }

  private static bool TryGetConfiguredCachePath(MountProfile profile, out string? cachePath, out string error)
  {
    cachePath = null;
    error = string.Empty;

    if (profile.MountOptions.TryGetValue("cache_dir", out string? fromUnderscore) &&
        !string.IsNullOrWhiteSpace(fromUnderscore))
    {
      cachePath = fromUnderscore.Trim();
      return true;
    }

    if (profile.MountOptions.TryGetValue("cache-dir", out string? fromDash) && !string.IsNullOrWhiteSpace(fromDash))
    {
      cachePath = fromDash.Trim();
      return true;
    }

    IReadOnlyList<string> args = ParseArguments(profile.ExtraOptions);
    for (int i = 0; i < args.Count; i++)
    {
      if (!string.Equals(args[i], "--cache-dir", StringComparison.Ordinal))
      {
        continue;
      }

      if (i == args.Count - 1 || string.IsNullOrWhiteSpace(args[i + 1]))
      {
        error = "Cache path check failed: '--cache-dir' is configured without a value.";
        return false;
      }

      cachePath = args[i + 1].Trim();
      return true;
    }

    return true;
  }

  private static bool TryResolveBinaryPath(string configuredBinary, out string resolvedPath, out string error)
  {
    resolvedPath = string.Empty;
    error = string.Empty;

    if (configuredBinary.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
        configuredBinary.IndexOf(Path.AltDirectorySeparatorChar) >= 0 || Path.IsPathRooted(configuredBinary))
    {
      if (!TryResolvePath(configuredBinary, out string candidatePath, out error))
      {
        error = $"invalid binary path: {error}";
        return false;
      }

      if (!File.Exists(candidatePath))
      {
        error = "file does not exist.";
        return false;
      }

      if (!IsExecutable(candidatePath))
      {
        error = "file is not executable.";
        return false;
      }

      resolvedPath = candidatePath;
      return true;
    }

    string pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
    foreach (string pathSegment in pathVariable.Split(
               Path.PathSeparator,
               StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
      string candidatePath = Path.Combine(pathSegment, configuredBinary);
      if (!File.Exists(candidatePath) || !IsExecutable(candidatePath))
      {
        continue;
      }

      resolvedPath = candidatePath;
      return true;
    }

    error = "binary not found in PATH.";
    return false;
  }

  private static bool TryEnsureWritableDirectory(string path, out string error)
  {
    error = string.Empty;

    try
    {
      Directory.CreateDirectory(path);
      string probeFile = Path.Combine(path, $".rclone-preflight-{Guid.NewGuid():N}.tmp");
      File.WriteAllText(probeFile, "ok");
      File.Delete(probeFile);
      return true;
    }
    catch (Exception ex)
    {
      error = ex.Message;
      return false;
    }
  }

  private static string ResolvePath(string path)
  {
    string trimmed = path.Trim();
    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    if (trimmed == "~")
    {
      trimmed = home;
    }
    else if (trimmed.StartsWith("~/", StringComparison.Ordinal))
    {
      trimmed = Path.Combine(home, trimmed[2..]);
    }

    return Path.GetFullPath(trimmed);
  }

  private static bool TryResolvePath(string inputPath, out string resolvedPath, out string error)
  {
    resolvedPath = string.Empty;
    error = string.Empty;

    try
    {
      resolvedPath = ResolvePath(inputPath);
      return true;
    }
    catch (Exception ex)
    {
      error = ex.Message;
      return false;
    }
  }

  private static bool IsExecutable(string filePath)
  {
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
      return true;
    }

    try
    {
      UnixFileMode mode = File.GetUnixFileMode(filePath);
      return mode.HasFlag(UnixFileMode.UserExecute)
             || mode.HasFlag(UnixFileMode.GroupExecute)
             || mode.HasFlag(UnixFileMode.OtherExecute);
    }
    catch
    {
      return false;
    }
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

  private static bool IsRcloneMountType(MountType mountType)
  {
    return mountType is MountType.RcloneAuto or MountType.RcloneFuse or MountType.RcloneNfs;
  }
}