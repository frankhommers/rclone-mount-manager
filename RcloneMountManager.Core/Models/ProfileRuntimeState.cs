using System;

namespace RcloneMountManager.Core.Models;

public sealed record ProfileRuntimeState(
  MountLifecycleState Lifecycle,
  MountHealthState Health,
  DateTimeOffset LastCheckedAt,
  string? ErrorText)
{
  public static ProfileRuntimeState Unknown { get; } = new(
    MountLifecycleState.Idle,
    MountHealthState.Unknown,
    DateTimeOffset.MinValue,
    null);
}