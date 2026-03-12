using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RcloneMountManager.Core.Models;
using RcloneMountManager.Core.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RcloneMountManager.Tests.Services;

public sealed class MountHealthServiceTests
{
  private static readonly DateTimeOffset FixedNow = new(2026, 2, 21, 0, 0, 0, TimeSpan.Zero);

  [Fact]
  public async Task VerifyAsync_MountedAndUsable_ReturnsHealthy()
  {
    MountProfile profile = CreateProfile();
    MountHealthService service = CreateService(
      (_, _) => Task.FromResult(true),
      _ => true,
      (_, _) => Task.FromResult(true));

    ProfileRuntimeState result = await service.VerifyAsync(profile, CancellationToken.None);

    Assert.Equal(MountLifecycleState.Mounted, result.Lifecycle);
    Assert.Equal(MountHealthState.Healthy, result.Health);
    Assert.Null(result.ErrorText);
    Assert.Equal(FixedNow, result.LastCheckedAt);
  }

  [Fact]
  public async Task VerifyAsync_MountedButUnusable_ReturnsDegraded()
  {
    MountProfile profile = CreateProfile();
    MountHealthService service = CreateService(
      (_, _) => Task.FromResult(true),
      _ => true,
      (_, _) => Task.FromResult(false));

    ProfileRuntimeState result = await service.VerifyAsync(profile, CancellationToken.None);

    Assert.Equal(MountLifecycleState.Mounted, result.Lifecycle);
    Assert.Equal(MountHealthState.Degraded, result.Health);
    Assert.Contains("not usable", result.ErrorText, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task VerifyAsync_MountedAndProbeTimeout_ReturnsDegraded()
  {
    MountProfile profile = CreateProfile();
    TaskCompletionSource<bool> timeoutTask = new(TaskCreationOptions.RunContinuationsAsynchronously);
    MountHealthService service = CreateService(
      (_, _) => Task.FromResult(true),
      _ => true,
      (_, _) => timeoutTask.Task,
      TimeSpan.FromMilliseconds(25));

    ProfileRuntimeState result = await service.VerifyAsync(profile, CancellationToken.None);

    Assert.Equal(MountHealthState.Degraded, result.Health);
    Assert.Contains("timed out", result.ErrorText, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task VerifyAsync_NotMountedNotRunning_ReturnsIdle()
  {
    MountProfile profile = CreateProfile();
    MountHealthService service = CreateService(
      (_, _) => Task.FromResult(false),
      _ => false,
      (_, _) => Task.FromResult(true));

    ProfileRuntimeState result = await service.VerifyAsync(profile, CancellationToken.None);

    Assert.Equal(MountLifecycleState.Idle, result.Lifecycle);
    Assert.Equal(MountHealthState.Unknown, result.Health);
    Assert.Null(result.ErrorText);
  }

  [Fact]
  public async Task VerifyAsync_NotMountedButRunning_ReturnsMounting()
  {
    MountProfile profile = CreateProfile();
    MountHealthService service = CreateService(
      (_, _) => Task.FromResult(false),
      _ => true,
      (_, _) => Task.FromResult(true));

    ProfileRuntimeState result = await service.VerifyAsync(profile, CancellationToken.None);

    Assert.Equal(MountLifecycleState.Mounting, result.Lifecycle);
    Assert.Equal(MountHealthState.Unknown, result.Health);
    Assert.Contains("not present yet", result.ErrorText, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task VerifyAsync_IsMountedProbeThrows_ReturnsTypedFailedResult()
  {
    MountProfile profile = CreateProfile();
    MountHealthService service = CreateService(
      (_, _) => throw new InvalidOperationException("mount command failed"),
      _ => false,
      (_, _) => Task.FromResult(true));

    ProfileRuntimeState result = await service.VerifyAsync(profile, CancellationToken.None);

    Assert.Equal(MountLifecycleState.Failed, result.Lifecycle);
    Assert.Equal(MountHealthState.Failed, result.Health);
    Assert.Contains("mount command failed", result.ErrorText, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task VerifyAsync_UsabilityProbeThrows_ReturnsTypedDegradedResult()
  {
    MountProfile profile = CreateProfile();
    MountHealthService service = CreateService(
      (_, _) => Task.FromResult(true),
      _ => true,
      (_, _) => throw new IOException("probe failed"));

    ProfileRuntimeState result = await service.VerifyAsync(profile, CancellationToken.None);

    Assert.Equal(MountLifecycleState.Mounted, result.Lifecycle);
    Assert.Equal(MountHealthState.Degraded, result.Health);
    Assert.Contains("probe failed", result.ErrorText, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task VerifyAllAsync_MultipleProfiles_ReturnsStateForEachProfile()
  {
    MountHealthService service = CreateService(
      (mountPoint, _) => Task.FromResult(mountPoint.Contains("mounted", StringComparison.Ordinal)),
      _ => true,
      (_, _) => Task.FromResult(true));

    MountProfile mounted = CreateProfile("/tmp/mounted");
    MountProfile missing = CreateProfile("/tmp/missing");

    IReadOnlyList<ProfileRuntimeState> states = await service.VerifyAllAsync(
      [mounted, missing],
      CancellationToken.None);

    Assert.Collection(
      states,
      first => Assert.Equal(MountHealthState.Healthy, first.Health),
      second => Assert.Equal(MountHealthState.Unknown, second.Health));
  }

  private static MountProfile CreateProfile(string mountPoint = "/tmp/test-mount")
  {
    return new MountProfile
    {
      Id = Guid.NewGuid().ToString("N"),
      Name = "profile",
      Source = "remote:bucket",
      MountPoint = mountPoint,
    };
  }

  private static MountHealthService CreateService(
    Func<string, CancellationToken, Task<bool>> isMountedProbe,
    Func<string, bool> isRunningProbe,
    Func<string, CancellationToken, Task<bool>> isMountUsableProbe,
    TimeSpan? mountProbeTimeout = null)
  {
    return new MountHealthService(
      NullLogger<MountHealthService>.Instance,
      isMountedProbe,
      isRunningProbe,
      isMountUsableProbe,
      mountProbeTimeout,
      () => FixedNow);
  }
}