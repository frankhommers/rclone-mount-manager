using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RcloneMountManager.Core.Models;
using RcloneMountManager.Core.Services;

namespace RcloneMountManager.Tests.Services;

public class MountManagerServiceTests
{
  private readonly MountManagerService _service = new(NullLogger<MountManagerService>.Instance);

  [Fact]
  public void GenerateScript_IncludesMountOptions_BoolTrue()
  {
    MountProfile profile = CreateProfile();
    profile.MountOptions["allow_other"] = "true";

    string script = _service.GenerateScript(profile);

    Assert.Contains("--allow-other", script);
  }

  [Fact]
  public void GenerateScript_SkipsBoolFalse()
  {
    MountProfile profile = CreateProfile();
    profile.MountOptions["debug_fuse"] = "false";

    string script = _service.GenerateScript(profile);

    Assert.DoesNotContain("--debug-fuse", script);
  }

  [Fact]
  public void GenerateScript_IncludesStringOption()
  {
    MountProfile profile = CreateProfile();
    profile.MountOptions["vfs_cache_mode"] = "full";

    string script = _service.GenerateScript(profile);

    Assert.Contains("--vfs-cache-mode", script);
    Assert.Contains("'full'", script);
  }

  [Fact]
  public void GenerateScript_IncludesDurationOption()
  {
    MountProfile profile = CreateProfile();
    profile.MountOptions["dir_cache_time"] = "15m";

    string script = _service.GenerateScript(profile);

    Assert.Contains("--dir-cache-time", script);
    Assert.Contains("'15m'", script);
  }

  [Fact]
  public void GenerateScript_IncludesSizeSuffixOption()
  {
    MountProfile profile = CreateProfile();
    profile.MountOptions["buffer_size"] = "128Mi";

    string script = _service.GenerateScript(profile);

    Assert.Contains("--buffer-size", script);
    Assert.Contains("'128Mi'", script);
  }

  [Fact]
  public void GenerateScript_IncludesNumericOption()
  {
    MountProfile profile = CreateProfile();
    profile.MountOptions["transfers"] = "8";

    string script = _service.GenerateScript(profile);

    Assert.Contains("--transfers", script);
    Assert.Contains("'8'", script);
  }

  [Fact]
  public void GenerateScript_IncludesMultipleOptions()
  {
    MountProfile profile = CreateProfile();
    profile.MountOptions["vfs_cache_mode"] = "full";
    profile.MountOptions["dir_cache_time"] = "10m";
    profile.MountOptions["buffer_size"] = "64Mi";
    profile.MountOptions["allow_other"] = "true";

    string script = _service.GenerateScript(profile);

    Assert.Contains("--vfs-cache-mode", script);
    Assert.Contains("--dir-cache-time", script);
    Assert.Contains("--buffer-size", script);
    Assert.Contains("--allow-other", script);
  }

  [Fact]
  public void GenerateScript_IncludesExtraOptions()
  {
    MountProfile profile = CreateProfile();
    profile.ExtraOptions = "--verbose --log-file /tmp/rclone.log";

    string script = _service.GenerateScript(profile);

    Assert.Contains("--verbose", script);
    Assert.Contains("--log-file", script);
  }

  [Fact]
  public void GenerateScript_SkipsEmptyMountOptionValues()
  {
    MountProfile profile = CreateProfile();
    profile.MountOptions["vfs_cache_mode"] = "";

    string script = _service.GenerateScript(profile);

    Assert.DoesNotContain("--vfs-cache-mode", script);
  }

  [Fact]
  public void GenerateScript_IncludesSourceAndMountPoint()
  {
    MountProfile profile = CreateProfile();

    string script = _service.GenerateScript(profile);

    Assert.Contains("'remote:media'", script);
    Assert.Contains("$MOUNT_POINT", script);
  }

  [Fact]
  public void GenerateScript_IncludesRcFlags_WhenRemoteControlEnabledAndRcAddrMissing()
  {
    MountProfile profile = CreateProfile();
    profile.EnableRemoteControl = true;
    profile.RcPort = 5572;

    string script = _service.GenerateScript(profile);

    Assert.Contains("--rc --rc-no-auth --rc-addr", script);
    Assert.Contains("'localhost:5572'", script);
  }

  [Fact]
  public void GenerateScript_SkipsRcAddrInjection_WhenRcAddrProvidedInExtraOptions()
  {
    MountProfile profile = CreateProfile();
    profile.EnableRemoteControl = true;
    profile.RcPort = 5572;
    profile.ExtraOptions = "--rc-addr localhost:60000";

    string script = _service.GenerateScript(profile);

    Assert.DoesNotContain("--rc --rc-no-auth --rc-addr", script);
  }

  [Fact]
  public void ResolveAbsoluteBinaryPath_ReturnsAbsolutePath_WhenAlreadyRooted()
  {
    string result = MountManagerService.ResolveAbsoluteBinaryPath("/usr/local/bin/rclone");

    Assert.Equal("/usr/local/bin/rclone", result);
  }

  [Fact]
  public void ResolveAbsoluteBinaryPath_ResolvesFromPath_WhenRelativeName()
  {
    string result = MountManagerService.ResolveAbsoluteBinaryPath("rclone");

    Assert.True(Path.IsPathRooted(result), $"Expected absolute path but got: {result}");
    Assert.EndsWith("rclone", result);
  }

  [Fact]
  public void ResolveAbsoluteBinaryPath_FallsBackToOriginal_WhenNotFound()
  {
    string result = MountManagerService.ResolveAbsoluteBinaryPath("nonexistent-binary-xyz");

    Assert.Equal("nonexistent-binary-xyz", result);
  }

  [Fact]
  public void ResolveAbsoluteBinaryPath_TreatsEmptyAsRclone()
  {
    string result = MountManagerService.ResolveAbsoluteBinaryPath("");

    Assert.True(
      Path.IsPathRooted(result) || result == "rclone",
      $"Expected resolved path or 'rclone' fallback but got: {result}");
  }

  [Fact]
  public void ExtractRcloneErrorDetail_FindsCriticalAndErrorLines()
  {
    string logTail = string.Join(
      "\n",
      "2026/03/25 11:59:14 NOTICE: Serving remote control on http://127.0.0.1:59116/",
      "2026/03/25 11:59:14 CRITICAL: Failed to create file system for \"Aivy-Box:/home/frank\": failed to read private key file: open /Users/frankhommers/.ssh/id_ed25519: no such file or directory");

    string result = MountManagerService.ExtractRcloneErrorDetail(logTail);

    Assert.Contains("CRITICAL", result);
    Assert.Contains("failed to read private key file", result);
    Assert.DoesNotContain("NOTICE", result);
  }

  [Fact]
  public void ExtractRcloneErrorDetail_ReturnsEmpty_WhenNoErrors()
  {
    string logTail = "2026/03/25 11:59:14 NOTICE: Serving remote control on http://127.0.0.1:59116/";

    string result = MountManagerService.ExtractRcloneErrorDetail(logTail);

    Assert.Empty(result);
  }

  [Fact]
  public void ExtractRcloneErrorDetail_ReturnsEmpty_WhenBlank()
  {
    string result = MountManagerService.ExtractRcloneErrorDetail("");

    Assert.Empty(result);
  }

  [Fact]
  public void ExtractRcloneErrorDetail_FindsMultipleErrorLines()
  {
    string logTail = string.Join(
      "\n",
      "2026/03/25 11:59:14 ERROR: first problem",
      "2026/03/25 11:59:14 NOTICE: something normal",
      "2026/03/25 11:59:15 ERROR: second problem");

    string result = MountManagerService.ExtractRcloneErrorDetail(logTail);

    Assert.Contains("first problem", result);
    Assert.Contains("second problem", result);
    Assert.DoesNotContain("something normal", result);
  }

  private static MountProfile CreateProfile()
  {
    return new MountProfile
    {
      Type = MountType.RcloneFuse,
      Source = "remote:media",
      MountPoint = "/tmp/test-mount",
      RcloneBinaryPath = "rclone",
      ExtraOptions = string.Empty,
      MountOptions = new Dictionary<string, string>(),
    };
  }
}