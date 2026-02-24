using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Concurrent;

namespace RcloneMountManager.Services;

public sealed class DiagnosticsSink : ILogEventSink
{
  public const string SystemProfileId = "_system";

  public static DiagnosticsSink Instance { get; } = new();

  private readonly ConcurrentQueue<LogEvent> _pending = new();
  private Action<LogEvent>? _handler;

  public void RegisterHandler(Action<LogEvent> handler)
  {
    _handler = handler;

    while (_pending.TryDequeue(out var buffered))
    {
      handler(buffered);
    }
  }

  public void Emit(LogEvent logEvent)
  {
    if (_handler is { } handler)
    {
      handler(logEvent);
    }
    else
    {
      _pending.Enqueue(logEvent);
    }
  }

  public static string ExtractProfileId(LogEvent logEvent)
  {
    if (logEvent.Properties.TryGetValue("ProfileId", out LogEventPropertyValue? value)
        && value is ScalarValue { Value: string profileId }
        && !string.IsNullOrWhiteSpace(profileId))
    {
      return profileId;
    }

    return SystemProfileId;
  }

  public static string RenderMessage(LogEvent logEvent)
  {
    return logEvent.RenderMessage();
  }
}
