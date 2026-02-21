using RcloneMountManager.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RcloneMountManager.Core.Services;

public sealed class MountHealthService
{
    private static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(2);

    private readonly Func<string, CancellationToken, Task<bool>> _isMountedProbe;
    private readonly Func<string, bool> _isRunningProbe;
    private readonly Func<string, CancellationToken, Task<bool>> _isMountUsableProbe;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _mountProbeTimeout;

    public MountHealthService(
        Func<string, CancellationToken, Task<bool>>? isMountedProbe = null,
        Func<string, bool>? isRunningProbe = null,
        Func<string, CancellationToken, Task<bool>>? isMountUsableProbe = null,
        TimeSpan? mountProbeTimeout = null,
        Func<DateTimeOffset>? clock = null)
    {
        var mountManagerService = new MountManagerService();
        _isMountedProbe = isMountedProbe ?? mountManagerService.IsMountedAsync;
        _isRunningProbe = isRunningProbe ?? mountManagerService.IsRunning;
        _isMountUsableProbe = isMountUsableProbe ?? ProbeMountPathUsabilityAsync;
        _mountProbeTimeout = mountProbeTimeout ?? DefaultProbeTimeout;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<ProfileRuntimeState> VerifyAsync(MountProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        bool isMounted;
        bool isRunning;

        try
        {
            isMounted = await _isMountedProbe(profile.MountPoint, cancellationToken);
            isRunning = _isRunningProbe(profile.MountPoint);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return UpdateRuntimeState(
                profile,
                new ProfileRuntimeState(MountLifecycleState.Failed, MountHealthState.Failed, _clock(), ex.Message));
        }

        if (!isMounted)
        {
            var lifecycle = isRunning ? MountLifecycleState.Mounting : MountLifecycleState.Failed;
            return UpdateRuntimeState(
                profile,
                new ProfileRuntimeState(lifecycle, MountHealthState.Failed, _clock(), "Mount is not present."));
        }

        try
        {
            var usable = await _isMountUsableProbe(profile.MountPoint, cancellationToken)
                .WaitAsync(_mountProbeTimeout, cancellationToken);

            if (usable)
            {
                return UpdateRuntimeState(
                    profile,
                    new ProfileRuntimeState(MountLifecycleState.Mounted, MountHealthState.Healthy, _clock(), null));
            }

            return UpdateRuntimeState(
                profile,
                new ProfileRuntimeState(MountLifecycleState.Mounted, MountHealthState.Degraded, _clock(), "Mount is present but not usable."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            var error = $"Mount usability probe timed out after {_mountProbeTimeout.TotalSeconds:0.##}s.";
            return UpdateRuntimeState(
                profile,
                new ProfileRuntimeState(MountLifecycleState.Mounted, MountHealthState.Degraded, _clock(), error));
        }
        catch (Exception ex)
        {
            return UpdateRuntimeState(
                profile,
                new ProfileRuntimeState(MountLifecycleState.Mounted, MountHealthState.Degraded, _clock(), ex.Message));
        }
    }

    public async Task<IReadOnlyList<ProfileRuntimeState>> VerifyAllAsync(IEnumerable<MountProfile> profiles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var states = new List<ProfileRuntimeState>();
        foreach (var profile in profiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            states.Add(await VerifyAsync(profile, cancellationToken));
        }

        return states;
    }

    private async Task<bool> ProbeMountPathUsabilityAsync(string mountPoint, CancellationToken cancellationToken)
    {
        var resolvedPath = ResolveMountPoint(mountPoint);

        return await Task.Run(() =>
        {
            using var enumerator = Directory.EnumerateFileSystemEntries(resolvedPath).GetEnumerator();
            _ = enumerator.MoveNext();
            return true;
        }, cancellationToken);
    }

    private static ProfileRuntimeState UpdateRuntimeState(MountProfile profile, ProfileRuntimeState state)
    {
        profile.RuntimeState = state;
        profile.IsMounted = state.Lifecycle is MountLifecycleState.Mounted;
        profile.LastStatus = state.Health.ToString();
        profile.IsRunning = state.Lifecycle is MountLifecycleState.Mounted or MountLifecycleState.Mounting;
        return state;
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
}
