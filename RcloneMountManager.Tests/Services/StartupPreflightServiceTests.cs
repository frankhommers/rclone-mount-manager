using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
  private readonly StartupPreflightService _service = new(NullLogger<StartupPreflightService>.Instance);
  private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"startup-preflight-tests-{Guid.NewGuid():N}");

  public StartupPreflightServiceTests()
  {
    Directory.CreateDirectory(_tempRoot);
  }

  [Fact]
  public async Task RunAsync_ValidProfile_PassesCriticalChecks()
  {
    MountProfile profile = CreateBaseProfile();

    StartupPreflightReport report = await _service.RunAsync(profile, CancellationToken.None);

    Assert.True(report.CriticalChecksPassed);
    Assert.Contains(report.Checks, check => check.CheckKey == StartupPreflightService.BinaryCheckKey && check.IsPass);
    Assert.Contains(
      report.Checks,
      check => check.CheckKey == StartupPreflightService.MountPathCheckKey && check.IsPass);
    Assert.Contains(
      report.Checks,
      check => check.CheckKey == StartupPreflightService.CredentialsCheckKey && check.IsPass);
    Assert.Contains(
      report.Checks,
      check => check.CheckKey == StartupPreflightService.SourceRemoteCheckKey && check.IsPass);
  }

  [Fact]
  public async Task RunAsync_MissingBinary_FailsCriticalWithMessage()
  {
    MountProfile profile = CreateBaseProfile();
    profile.RcloneBinaryPath = Path.Combine(_tempRoot, "missing-rclone");

    StartupPreflightReport report = await _service.RunAsync(profile, CancellationToken.None);

    StartupCheckResult binaryCheck = Assert.Single(
      report.Checks,
      check => check.CheckKey == StartupPreflightService.BinaryCheckKey);
    Assert.False(report.CriticalChecksPassed);
    Assert.True(binaryCheck.IsCriticalFailure);
    Assert.Contains("Could not resolve executable rclone binary", binaryCheck.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task RunAsync_InvalidMountPath_FailsCritical()
  {
    MountProfile profile = CreateBaseProfile();
    profile.MountPoint = "\0invalid";

    StartupPreflightReport report = await _service.RunAsync(profile, CancellationToken.None);

    StartupCheckResult mountCheck = Assert.Single(
      report.Checks,
      check => check.CheckKey == StartupPreflightService.MountPathCheckKey);
    Assert.False(report.CriticalChecksPassed);
    Assert.True(mountCheck.IsCriticalFailure);
    Assert.Contains("Mount path", mountCheck.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task RunAsync_InvalidCachePathWithCacheOption_FailsCritical()
  {
    MountProfile profile = CreateBaseProfile();
    profile.MountOptions["cache_dir"] = "\0invalid-cache";

    StartupPreflightReport report = await _service.RunAsync(profile, CancellationToken.None);

    StartupCheckResult cacheCheck = Assert.Single(
      report.Checks,
      check => check.CheckKey == StartupPreflightService.CachePathCheckKey);
    Assert.False(report.CriticalChecksPassed);
    Assert.True(cacheCheck.IsCriticalFailure);
    Assert.Contains("Cache path", cacheCheck.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task RunAsync_QuickConnectMissingCredentials_FailsCritical()
  {
    MountProfile profile = CreateBaseProfile();
    profile.QuickConnectMode = QuickConnectMode.WebDav;
    profile.QuickConnectEndpoint = "https://example.test/webdav";
    profile.QuickConnectPassword = string.Empty;

    StartupPreflightReport report = await _service.RunAsync(profile, CancellationToken.None);

    StartupCheckResult credentialsCheck = Assert.Single(
      report.Checks,
      check => check.CheckKey == StartupPreflightService.CredentialsCheckKey);
    Assert.False(report.CriticalChecksPassed);
    Assert.True(credentialsCheck.IsCriticalFailure);
    Assert.Contains("credentials are unavailable", credentialsCheck.Message, StringComparison.Ordinal);
  }

  [Theory]
  [InlineData("remote:bucket", true, "remote")]
  [InlineData("Passwords:/", true, "Passwords")]
  [InlineData("my-remote:path/to/folder", true, "my-remote")]
  [InlineData("", false, "")]
  [InlineData("   ", false, "")]
  [InlineData(":webdav:/path", false, "")]
  [InlineData("no-colon", false, "")]
  public void TryExtractRemoteAlias_ExtractsCorrectly(string source, bool expectedResult, string expectedAlias)
  {
    bool result = StartupPreflightService.TryExtractRemoteAlias(source, out string alias);

    Assert.Equal(expectedResult, result);
    Assert.Equal(expectedAlias, alias);
  }

  [Fact]
  public async Task RunAsync_QuickConnectProfile_SkipsSourceRemoteCheck()
  {
    MountProfile profile = CreateBaseProfile();
    profile.QuickConnectMode = QuickConnectMode.Sftp;
    profile.QuickConnectEndpoint = "example.test";
    profile.QuickConnectPassword = "secret";

    StartupPreflightReport report = await _service.RunAsync(profile, CancellationToken.None);

    StartupCheckResult sourceCheck = Assert.Single(
      report.Checks,
      check => check.CheckKey == StartupPreflightService.SourceRemoteCheckKey);
    Assert.True(sourceCheck.IsPass);
    Assert.Contains("skipped", sourceCheck.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task RunAsync_NfsProfile_SkipsSourceRemoteCheck()
  {
    MountProfile profile = CreateBaseProfile();
    profile.Type = MountType.MacOsNfs;
    profile.Source = "server:/export";

    StartupPreflightReport report = await _service.RunAsync(profile, CancellationToken.None);

    StartupCheckResult sourceCheck = Assert.Single(
      report.Checks,
      check => check.CheckKey == StartupPreflightService.SourceRemoteCheckKey);
    Assert.True(sourceCheck.IsPass);
    Assert.Contains("skipped", sourceCheck.Message, StringComparison.OrdinalIgnoreCase);
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
    string binaryPath = CreateExecutableBinary();
    string mountPath = Path.Combine(_tempRoot, "mount");

    return new MountProfile
    {
      Type = MountType.RcloneAuto,
      Source = "remote:bucket",
      MountPoint = mountPath,
      RcloneBinaryPath = binaryPath,
      QuickConnectMode = QuickConnectMode.None,
      MountOptions = new Dictionary<string, string>(),
    };
  }

  private string CreateExecutableBinary(string remoteName = "remote")
  {
    string binaryPath = Path.Combine(_tempRoot, "rclone-test-binary");
    File.WriteAllText(binaryPath, $"#!/bin/sh\necho \"{remoteName}:\"\nexit 0\n");

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