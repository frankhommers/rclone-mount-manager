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
        var profile = CreateProfile();
        var service = CreateService(
            isMountedProbe: (_, _) => Task.FromResult(true),
            isRunningProbe: _ => true,
            isMountUsableProbe: (_, _) => Task.FromResult(true));

        var result = await service.VerifyAsync(profile, CancellationToken.None);

        Assert.Equal(MountLifecycleState.Mounted, result.Lifecycle);
        Assert.Equal(MountHealthState.Healthy, result.Health);
        Assert.Null(result.ErrorText);
        Assert.Equal(FixedNow, result.LastCheckedAt);
        Assert.Equal(result, profile.RuntimeState);
    }

    [Fact]
    public async Task VerifyAsync_MountedButUnusable_ReturnsDegraded()
    {
        var profile = CreateProfile();
        var service = CreateService(
            isMountedProbe: (_, _) => Task.FromResult(true),
            isRunningProbe: _ => true,
            isMountUsableProbe: (_, _) => Task.FromResult(false));

        var result = await service.VerifyAsync(profile, CancellationToken.None);

        Assert.Equal(MountLifecycleState.Mounted, result.Lifecycle);
        Assert.Equal(MountHealthState.Degraded, result.Health);
        Assert.Contains("not usable", result.ErrorText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyAsync_MountedAndProbeTimeout_ReturnsDegraded()
    {
        var profile = CreateProfile();
        var timeoutTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = CreateService(
            isMountedProbe: (_, _) => Task.FromResult(true),
            isRunningProbe: _ => true,
            isMountUsableProbe: (_, _) => timeoutTask.Task,
            mountProbeTimeout: TimeSpan.FromMilliseconds(25));

        var result = await service.VerifyAsync(profile, CancellationToken.None);

        Assert.Equal(MountHealthState.Degraded, result.Health);
        Assert.Contains("timed out", result.ErrorText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyAsync_NotMountedNotRunning_ReturnsIdle()
    {
        var profile = CreateProfile();
        var service = CreateService(
            isMountedProbe: (_, _) => Task.FromResult(false),
            isRunningProbe: _ => false,
            isMountUsableProbe: (_, _) => Task.FromResult(true));

        var result = await service.VerifyAsync(profile, CancellationToken.None);

        Assert.Equal(MountLifecycleState.Idle, result.Lifecycle);
        Assert.Equal(MountHealthState.Unknown, result.Health);
        Assert.Null(result.ErrorText);
    }

    [Fact]
    public async Task VerifyAsync_NotMountedButRunning_ReturnsMounting()
    {
        var profile = CreateProfile();
        var service = CreateService(
            isMountedProbe: (_, _) => Task.FromResult(false),
            isRunningProbe: _ => true,
            isMountUsableProbe: (_, _) => Task.FromResult(true));

        var result = await service.VerifyAsync(profile, CancellationToken.None);

        Assert.Equal(MountLifecycleState.Mounting, result.Lifecycle);
        Assert.Equal(MountHealthState.Unknown, result.Health);
        Assert.Contains("not present yet", result.ErrorText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyAsync_IsMountedProbeThrows_ReturnsTypedFailedResult()
    {
        var profile = CreateProfile();
        var service = CreateService(
            isMountedProbe: (_, _) => throw new InvalidOperationException("mount command failed"),
            isRunningProbe: _ => false,
            isMountUsableProbe: (_, _) => Task.FromResult(true));

        var result = await service.VerifyAsync(profile, CancellationToken.None);

        Assert.Equal(MountLifecycleState.Failed, result.Lifecycle);
        Assert.Equal(MountHealthState.Failed, result.Health);
        Assert.Contains("mount command failed", result.ErrorText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyAsync_UsabilityProbeThrows_ReturnsTypedDegradedResult()
    {
        var profile = CreateProfile();
        var service = CreateService(
            isMountedProbe: (_, _) => Task.FromResult(true),
            isRunningProbe: _ => true,
            isMountUsableProbe: (_, _) => throw new IOException("probe failed"));

        var result = await service.VerifyAsync(profile, CancellationToken.None);

        Assert.Equal(MountLifecycleState.Mounted, result.Lifecycle);
        Assert.Equal(MountHealthState.Degraded, result.Health);
        Assert.Contains("probe failed", result.ErrorText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyAllAsync_MultipleProfiles_ReturnsStateForEachProfile()
    {
        var service = CreateService(
            isMountedProbe: (mountPoint, _) => Task.FromResult(mountPoint.Contains("mounted", StringComparison.Ordinal)),
            isRunningProbe: _ => true,
            isMountUsableProbe: (_, _) => Task.FromResult(true));

        var mounted = CreateProfile("/tmp/mounted");
        var missing = CreateProfile("/tmp/missing");

        var states = await service.VerifyAllAsync([mounted, missing], CancellationToken.None);

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
            isMountedProbe: isMountedProbe,
            isRunningProbe: isRunningProbe,
            isMountUsableProbe: isMountUsableProbe,
            mountProbeTimeout: mountProbeTimeout,
            clock: () => FixedNow);
    }
}
