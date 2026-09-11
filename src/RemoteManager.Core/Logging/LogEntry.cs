using System.Text;

namespace RemoteManager.Core.Logging;

public class LogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public LogLevel Level { get; init; } = LogLevel.Info;
    public string Category { get; init; } = "General";
    public string Message { get; init; } = string.Empty;
    public Exception? Exception { get; init; }
    public string? ExceptionDetails { get; init; }

    public string FormattedTimestamp => Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");

    public string LevelDisplay => Level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Info  => "INFO ",
        LogLevel.Warn  => "WARN ",
        LogLevel.Error => "ERROR",
        LogLevel.Fatal => "FATAL",
        _ => Level.ToString().ToUpperInvariant()
    };

    public string LevelBadgeText => Level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Info  => "INFO",
        LogLevel.Warn  => "WARNING",
        LogLevel.Error => "ERROR",
        LogLevel.Fatal => "FATAL",
        _ => Level.ToString()
    };

    public string LevelBadgeColor => Level switch
    {
        LogLevel.Trace => "#888888",
        LogLevel.Debug => "#6C7A89",
        LogLevel.Info  => "#0078D4",
        LogLevel.Warn  => "#D83B01",
        LogLevel.Error => "#E81123",
        LogLevel.Fatal => "#A80000",
        _ => "#0078D4"
    };

    public string LevelBadgeBackground => Level switch
    {
        LogLevel.Trace => "#1E888888",
        LogLevel.Debug => "#1E6C7A89",
        LogLevel.Info  => "#1E0078D4",
        LogLevel.Warn  => "#1ED83B01",
        LogLevel.Error => "#1EE81123",
        LogLevel.Fatal => "#22A80000",
        _ => "#1E0078D4"
    };

    public bool HasException => Exception != null || !string.IsNullOrWhiteSpace(ExceptionDetails);

    public string FullExceptionString => ExceptionDetails ?? Exception?.ToString() ?? string.Empty;

    public string ToFileLogLine()
    {
        var sb = new StringBuilder();
        sb.Append($"[{FormattedTimestamp}] [{LevelDisplay.Trim()}] [{Category}] {Message}");
        if (HasException)
        {
            sb.AppendLine();
            sb.Append("    Exception: ").Append(FullExceptionString);
        }
        return sb.ToString();
    }

    public override string ToString() => ToFileLogLine();
}
