using RemoteManager.Core.Models;

namespace RemoteManager.Core.Logging;

public interface ILogService
{
    string LogsDirectory { get; }
    AppSettings? CurrentSettings { get; }

    void Configure(AppSettings settings);
    void Log(LogLevel level, string category, string message, Exception? ex = null);

    void Trace(string category, string message);
    void Debug(string category, string message);
    void Info(string category, string message);
    void Warn(string category, string message, Exception? ex = null);
    void Error(string category, string message, Exception? ex = null);
    void Fatal(string category, string message, Exception? ex = null);

    event Action<LogEntry>? LogReceived;

    IReadOnlyList<LogEntry> GetRecentLogs(int count = 2000);
    IReadOnlyList<string> GetAvailableLogFiles();
    Task<IReadOnlyList<LogEntry>> ReadLogFileAsync(string filePath);
    Task FlushAsync();
}
