using System;

namespace RcloneMountManager.Core.Models;

public enum ProfileLogCategory
{
    Startup,
    RuntimeRefresh,
    ManualStart,
    ManualStop,
    General,
}

public enum ProfileLogStage
{
    Initialization,
    Verification,
    Execution,
    Completion,
}

public enum ProfileLogSeverity
{
    Information,
    Warning,
    Error,
}

public sealed record ProfileLogEvent(
    string ProfileId,
    DateTimeOffset Timestamp,
    ProfileLogCategory Category,
    ProfileLogStage Stage,
    ProfileLogSeverity Severity,
    string Message,
    string? Error = null);
