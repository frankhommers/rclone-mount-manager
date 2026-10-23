namespace RcloneMountManager.Core.Models;

public sealed record LaunchAgentRestartPolicy(
  int ThrottleIntervalSeconds,
  int InitialBackoffSeconds,
  int MaxBackoffSeconds,
  int HealthyRunSeconds)
{
  public static LaunchAgentRestartPolicy Default { get; } = new(
    ThrottleIntervalSeconds: 30,
    InitialBackoffSeconds: 30,
    MaxBackoffSeconds: 900,
    HealthyRunSeconds: 120);
}
