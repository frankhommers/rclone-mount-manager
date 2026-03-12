using System;

namespace RcloneMountManager.Core.Models;

public enum StartupCheckSeverity
{
  Pass,
  Warning,
  Critical,
}

public sealed record StartupCheckResult(string CheckKey, StartupCheckSeverity Severity, string Message)
{
  public bool IsPass => Severity is StartupCheckSeverity.Pass;

  public bool IsCriticalFailure => Severity is StartupCheckSeverity.Critical;

  public bool IsWarningFailure => Severity is StartupCheckSeverity.Warning;

  public static StartupCheckResult Pass(string checkKey, string message)
  {
    return new StartupCheckResult(ValidateCheckKey(checkKey), StartupCheckSeverity.Pass, ValidateMessage(message));
  }

  public static StartupCheckResult Warning(string checkKey, string message)
  {
    return new StartupCheckResult(ValidateCheckKey(checkKey), StartupCheckSeverity.Warning, ValidateMessage(message));
  }

  public static StartupCheckResult Critical(string checkKey, string message)
  {
    return new StartupCheckResult(ValidateCheckKey(checkKey), StartupCheckSeverity.Critical, ValidateMessage(message));
  }

  private static string ValidateCheckKey(string checkKey)
  {
    if (string.IsNullOrWhiteSpace(checkKey))
    {
      throw new ArgumentException("Check key is required.", nameof(checkKey));
    }

    return checkKey.Trim();
  }

  private static string ValidateMessage(string message)
  {
    if (string.IsNullOrWhiteSpace(message))
    {
      throw new ArgumentException("Message is required.", nameof(message));
    }

    return message.Trim();
  }
}