using RcloneMountManager.Core.Models;
using RcloneMountManager.Core.Services;

namespace RcloneMountManager.Tests.Services;

public class MountManagerServiceTests
{
    private readonly MountManagerService _service = new();

    [Fact]
    public void GenerateScript_IncludesMountOptions_BoolTrue()
    {
        var profile = CreateProfile();
        profile.MountOptions["allow_other"] = "true";

        var script = _service.GenerateScript(profile);

        Assert.Contains("--allow-other", script);
    }

    [Fact]
    public void GenerateScript_SkipsBoolFalse()
    {
        var profile = CreateProfile();
        profile.MountOptions["debug_fuse"] = "false";

        var script = _service.GenerateScript(profile);

        Assert.DoesNotContain("--debug-fuse", script);
    }

    [Fact]
    public void GenerateScript_IncludesStringOption()
    {
        var profile = CreateProfile();
        profile.MountOptions["vfs_cache_mode"] = "full";

        var script = _service.GenerateScript(profile);

        Assert.Contains("--vfs-cache-mode", script);
        Assert.Contains("'full'", script);
    }

    [Fact]
    public void GenerateScript_IncludesDurationOption()
    {
        var profile = CreateProfile();
        profile.MountOptions["dir_cache_time"] = "15m";

        var script = _service.GenerateScript(profile);

        Assert.Contains("--dir-cache-time", script);
        Assert.Contains("'15m'", script);
    }

    [Fact]
    public void GenerateScript_IncludesSizeSuffixOption()
    {
        var profile = CreateProfile();
        profile.MountOptions["buffer_size"] = "128Mi";

        var script = _service.GenerateScript(profile);

        Assert.Contains("--buffer-size", script);
        Assert.Contains("'128Mi'", script);
    }

    [Fact]
    public void GenerateScript_IncludesNumericOption()
    {
        var profile = CreateProfile();
        profile.MountOptions["transfers"] = "8";

        var script = _service.GenerateScript(profile);

        Assert.Contains("--transfers", script);
        Assert.Contains("'8'", script);
    }

    [Fact]
    public void GenerateScript_IncludesMultipleOptions()
    {
        var profile = CreateProfile();
        profile.MountOptions["vfs_cache_mode"] = "full";
        profile.MountOptions["dir_cache_time"] = "10m";
        profile.MountOptions["buffer_size"] = "64Mi";
        profile.MountOptions["allow_other"] = "true";

        var script = _service.GenerateScript(profile);

        Assert.Contains("--vfs-cache-mode", script);
        Assert.Contains("--dir-cache-time", script);
        Assert.Contains("--buffer-size", script);
        Assert.Contains("--allow-other", script);
    }

    [Fact]
    public void GenerateScript_IncludesExtraOptions()
    {
        var profile = CreateProfile();
        profile.ExtraOptions = "--verbose --log-file /tmp/rclone.log";

        var script = _service.GenerateScript(profile);

        Assert.Contains("--verbose", script);
        Assert.Contains("--log-file", script);
    }

    [Fact]
    public void GenerateScript_SkipsEmptyMountOptionValues()
    {
        var profile = CreateProfile();
        profile.MountOptions["vfs_cache_mode"] = "";

        var script = _service.GenerateScript(profile);

        Assert.DoesNotContain("--vfs-cache-mode", script);
    }

    [Fact]
    public void GenerateScript_IncludesSourceAndMountPoint()
    {
        var profile = CreateProfile();

        var script = _service.GenerateScript(profile);

        Assert.Contains("'remote:media'", script);
        Assert.Contains("$MOUNT_POINT", script);
    }

    [Fact]
    public void GenerateScript_IncludesRcFlags_WhenRemoteControlEnabledAndRcAddrMissing()
    {
        var profile = CreateProfile();
        profile.EnableRemoteControl = true;
        profile.RcPort = 5572;

        var script = _service.GenerateScript(profile);

        Assert.Contains("--rc --rc-no-auth --rc-addr", script);
        Assert.Contains("'localhost:5572'", script);
    }

    [Fact]
    public void GenerateScript_SkipsRcAddrInjection_WhenRcAddrProvidedInExtraOptions()
    {
        var profile = CreateProfile();
        profile.EnableRemoteControl = true;
        profile.RcPort = 5572;
        profile.ExtraOptions = "--rc-addr localhost:60000";

        var script = _service.GenerateScript(profile);

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
