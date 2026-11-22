using Serilog.Core;
using Serilog.Events;

namespace Generral.Core.Instrumentation;

public sealed record LogEntry(int Id, DateTime Time, string Level, string Logger, string Message, string? Exception);

/// <summary>
/// The last <see cref="Capacity"/> Info+ log events, in memory, for System → Events. Sonarr keeps them in a database table; Generral
/// doesn't (they are in the log files anyway), so the list starts empty after a restart.
/// </summary>
public sealed class RecentLogSink : ILogEventSink
{
    public const int Capacity = 1000;

    private readonly LinkedList<LogEntry> _entries = new();
    private readonly Lock _lock = new();
    private int _lastId;

    public static RecentLogSink Instance { get; } = new();

    public void Emit(LogEvent logEvent)
    {
        var logger = logEvent.Properties.TryGetValue("SourceContext", out var context) && context is ScalarValue { Value: string name }
            ? name[(name.LastIndexOf('.') + 1)..]
            : "";
        var message = CleanseLogMessage.Cleanse(logEvent.RenderMessage());
        var exception = logEvent.Exception is null ? null : CleanseLogMessage.Cleanse(logEvent.Exception.ToString());
        lock (_lock)
        {
            _entries.AddFirst(new LogEntry(++_lastId, logEvent.Timestamp.UtcDateTime, Level(logEvent.Level), logger, message, exception));
            if (_entries.Count > Capacity)
            {
                _entries.RemoveLast();
            }
        }
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<LogEntry> All()
    {
        lock (_lock)
        {
            return _entries.ToList();
        }
    }

    private static string Level(LogEventLevel level) => level switch
    {
        LogEventLevel.Warning => "warn",
        LogEventLevel.Error => "error",
        LogEventLevel.Fatal => "fatal",
        _ => "info",
    };
}
