using RcloneMountManager.Core.Models;
using RcloneMountManager.Core.Services;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace RcloneMountManager.Tests.Services;

public sealed class StartupPreflightServiceTests : IDisposable
{
    private readonly StartupPreflightService _service = new();
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"startup-preflight-tests-{Guid.NewGuid():N}");

    public StartupPreflightServiceTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public async Task RunAsync_ValidProfile_PassesCriticalChecks()
    {
        var profile = CreateBaseProfile();

        var report = await _service.RunAsync(profile, CancellationToken.None);

        Assert.True(report.CriticalChecksPassed);
        Assert.Contains(report.Checks, check => check.CheckKey == StartupPreflightService.BinaryCheckKey && check.IsPass);
        Assert.Contains(report.Checks, check => check.CheckKey == StartupPreflightService.MountPathCheckKey && check.IsPass);
        Assert.Contains(report.Checks, check => check.CheckKey == StartupPreflightService.CredentialsCheckKey && check.IsPass);
    }

    [Fact]
    public async Task RunAsync_MissingBinary_FailsCriticalWithMessage()
    {
        var profile = CreateBaseProfile();
        profile.RcloneBinaryPath = Path.Combine(_tempRoot, "missing-rclone");

        var report = await _service.RunAsync(profile, CancellationToken.None);

        var binaryCheck = Assert.Single(report.Checks, check => check.CheckKey == StartupPreflightService.BinaryCheckKey);
        Assert.False(report.CriticalChecksPassed);
        Assert.True(binaryCheck.IsCriticalFailure);
        Assert.Contains("Could not resolve executable rclone binary", binaryCheck.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_InvalidMountPath_FailsCritical()
    {
        var profile = CreateBaseProfile();
        profile.MountPoint = "\0invalid";

        var report = await _service.RunAsync(profile, CancellationToken.None);

        var mountCheck = Assert.Single(report.Checks, check => check.CheckKey == StartupPreflightService.MountPathCheckKey);
        Assert.False(report.CriticalChecksPassed);
        Assert.True(mountCheck.IsCriticalFailure);
        Assert.Contains("Mount path", mountCheck.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_InvalidCachePathWithCacheOption_FailsCritical()
    {
        var profile = CreateBaseProfile();
        profile.MountOptions["cache_dir"] = "\0invalid-cache";

        var report = await _service.RunAsync(profile, CancellationToken.None);

        var cacheCheck = Assert.Single(report.Checks, check => check.CheckKey == StartupPreflightService.CachePathCheckKey);
        Assert.False(report.CriticalChecksPassed);
        Assert.True(cacheCheck.IsCriticalFailure);
        Assert.Contains("Cache path", cacheCheck.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_QuickConnectMissingCredentials_FailsCritical()
    {
        var profile = CreateBaseProfile();
        profile.QuickConnectMode = QuickConnectMode.WebDav;
        profile.QuickConnectEndpoint = "https://example.test/webdav";
        profile.QuickConnectPassword = string.Empty;

        var report = await _service.RunAsync(profile, CancellationToken.None);

        var credentialsCheck = Assert.Single(report.Checks, check => check.CheckKey == StartupPreflightService.CredentialsCheckKey);
        Assert.False(report.CriticalChecksPassed);
        Assert.True(credentialsCheck.IsCriticalFailure);
        Assert.Contains("credentials are unavailable", credentialsCheck.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }
        catch
        {
            // Best effort cleanup for temporary test files.
        }
    }

    private MountProfile CreateBaseProfile()
    {
        var binaryPath = CreateExecutableBinary();
        var mountPath = Path.Combine(_tempRoot, "mount");

        return new MountProfile
        {
            Type = MountType.RcloneAuto,
            Source = "remote:bucket",
            MountPoint = mountPath,
            RcloneBinaryPath = binaryPath,
            QuickConnectMode = QuickConnectMode.None,
            MountOptions = new(),
        };
    }

    private string CreateExecutableBinary()
    {
        var binaryPath = Path.Combine(_tempRoot, "rclone-test-binary");
        File.WriteAllText(binaryPath, "#!/bin/sh\nexit 0\n");

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(
                binaryPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        return binaryPath;
    }
}
