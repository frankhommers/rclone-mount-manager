using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Serilog.Core;
using Serilog.Events;

namespace RcloneMountManager.GUI.Services;

public sealed class DiagnosticsSink : ILogEventSink
{
  public const string SystemProfileId = "_system";

  public static DiagnosticsSink Instance { get; } = new();

  private readonly ConcurrentQueue<LogEvent> _pending = new();
  private readonly List<Action<LogEvent>> _handlers = new();
  private readonly object _handlersLock = new();

  public void RegisterHandler(Action<LogEvent> handler)
  {
    lock (_handlersLock)
    {
      _handlers.Add(handler);
    }

    while (_pending.TryDequeue(out LogEvent? buffered))
    {
      handler(buffered);
    }
  }

  public void UnregisterHandler(Action<LogEvent> handler)
  {
    lock (_handlersLock)
    {
      _handlers.Remove(handler);
    }
  }

  public void Emit(LogEvent logEvent)
  {
    Action<LogEvent>[] snapshot;
    lock (_handlersLock)
    {
      if (_handlers.Count == 0)
      {
        _pending.Enqueue(logEvent);
        return;
      }

      snapshot = _handlers.ToArray();
    }

    foreach (Action<LogEvent> handler in snapshot)
    {
      handler(logEvent);
    }
  }

  public static string ExtractProfileId(LogEvent logEvent)
  {
    if (logEvent.Properties.TryGetValue("ProfileId", out LogEventPropertyValue? value)
        && value is ScalarValue {Value: string profileId}
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