using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

  private readonly ILogger<MountHealthService> _logger;
  private readonly Func<string, CancellationToken, Task<bool>> _isMountedProbe;
  private readonly Func<string, bool> _isRunningProbe;
  private readonly Func<string, CancellationToken, Task<bool>> _isMountUsableProbe;
  private readonly Func<DateTimeOffset> _clock;
  private readonly TimeSpan _mountProbeTimeout;

  public MountHealthService(
    ILogger<MountHealthService> logger,
    Func<string, CancellationToken, Task<bool>>? isMountedProbe = null,
    Func<string, bool>? isRunningProbe = null,
    Func<string, CancellationToken, Task<bool>>? isMountUsableProbe = null,
    TimeSpan? mountProbeTimeout = null,
    Func<DateTimeOffset>? clock = null)
  {
    _logger = logger;
    MountManagerService mountManagerService = new(NullLogger<MountManagerService>.Instance);
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
      return ReturnState(
        new ProfileRuntimeState(MountLifecycleState.Failed, MountHealthState.Failed, _clock(), ex.Message));
    }

    if (!isMounted)
    {
      if (isRunning)
      {
        return ReturnState(
          new ProfileRuntimeState(
            MountLifecycleState.Mounting,
            MountHealthState.Unknown,
            _clock(),
            "Mount is not present yet."));
      }

      return ReturnState(
        new ProfileRuntimeState(MountLifecycleState.Idle, MountHealthState.Unknown, _clock(), null));
    }

    try
    {
      bool usable = await _isMountUsableProbe(profile.MountPoint, cancellationToken)
        .WaitAsync(_mountProbeTimeout, cancellationToken);

      if (usable)
      {
        return ReturnState(
          new ProfileRuntimeState(MountLifecycleState.Mounted, MountHealthState.Healthy, _clock(), null));
      }

      return ReturnState(
        new ProfileRuntimeState(
          MountLifecycleState.Mounted,
          MountHealthState.Degraded,
          _clock(),
          "Mount is present but not usable."));
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (TimeoutException)
    {
      string error = $"Mount usability probe timed out after {_mountProbeTimeout.TotalSeconds:0.##}s.";
      return ReturnState(
        new ProfileRuntimeState(MountLifecycleState.Mounted, MountHealthState.Degraded, _clock(), error));
    }
    catch (Exception ex)
    {
      return ReturnState(
        new ProfileRuntimeState(MountLifecycleState.Mounted, MountHealthState.Degraded, _clock(), ex.Message));
    }
  }

  public async Task<IReadOnlyList<ProfileRuntimeState>> VerifyAllAsync(
    IEnumerable<MountProfile> profiles,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(profiles);

    IList<MountProfile> profileList = profiles as IList<MountProfile> ?? new List<MountProfile>(profiles);
    Task<ProfileRuntimeState>[] tasks = new Task<ProfileRuntimeState>[profileList.Count];

    for (int index = 0; index < profileList.Count; index++)
    {
      MountProfile profile = profileList[index];
      tasks[index] = VerifyAsync(profile, cancellationToken);
    }

    ProfileRuntimeState[] states = await Task.WhenAll(tasks);
    return states;
  }

  private async Task<bool> ProbeMountPathUsabilityAsync(string mountPoint, CancellationToken cancellationToken)
  {
    string resolvedPath = ResolveMountPoint(mountPoint);

    return await Task.Run(
      () =>
      {
        try
        {
          using IEnumerator<string> enumerator = Directory.EnumerateFileSystemEntries(resolvedPath).GetEnumerator();
          _ = enumerator.MoveNext();
          return true;
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or IOException)
        {
          return false;
        }
      },
      cancellationToken);
  }

  private static ProfileRuntimeState ReturnState(ProfileRuntimeState state)
  {
    return state;
  }

  private static string ResolveMountPoint(string mountPoint)
  {
    if (string.IsNullOrWhiteSpace(mountPoint))
    {
      return mountPoint;
    }

    string trimmed = mountPoint.Trim();
    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

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