using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace RemoteManager.Core.Logging;

public sealed class LogEngine : ILogService, IAsyncDisposable, IDisposable
{
    private static readonly Lazy<LogEngine> _instance = new(() => new LogEngine());
    public static LogEngine Instance => _instance.Value;

    private string _logsDirectory;
    private readonly Channel<LogEntry> _channel;
    private readonly Task _writerTask;
    private readonly object _bufferLock = new();
    private readonly LinkedList<LogEntry> _recentBuffer = new();
    private const int MaxBufferSize = 2000;

    private StreamWriter? _currentWriter;
    private string? _currentDateString;
    private RemoteManager.Core.Models.AppSettings? _settings;

    public string LogsDirectory { get => _logsDirectory; internal set => _logsDirectory = value; }
    public RemoteManager.Core.Models.AppSettings? CurrentSettings => _settings;
    internal static Func<string>? BaseLogsDirectoryResolver { get; set; }

    public event Action<LogEntry>? LogReceived;

    public void Configure(RemoteManager.Core.Models.AppSettings settings)
    {
        _settings = settings;
    }

    public bool IsModuleEnabled(string category)
    {
        if (_settings == null) return true;
        if (!_settings.IsLoggingEnabled) return false;

        if (category.StartsWith("Protocol.RDP", StringComparison.OrdinalIgnoreCase))
            return _settings.LogRdp;
        if (category.StartsWith("Protocol.SSH", StringComparison.OrdinalIgnoreCase))
            return _settings.LogSsh;
        if (category.StartsWith("Protocol.VNC", StringComparison.OrdinalIgnoreCase))
            return _settings.LogVnc;
        if (category.StartsWith("Protocol.Web", StringComparison.OrdinalIgnoreCase))
            return _settings.LogWeb;
        if (category.StartsWith("Database", StringComparison.OrdinalIgnoreCase))
            return _settings.LogDatabase;
        if (category.StartsWith("Security", StringComparison.OrdinalIgnoreCase))
            return _settings.LogSecurity;
        if (category.StartsWith("App", StringComparison.OrdinalIgnoreCase) ||
            category.StartsWith("UI", StringComparison.OrdinalIgnoreCase) ||
            category.StartsWith("Session", StringComparison.OrdinalIgnoreCase))
        {
            return _settings.LogAppLifecycle;
        }

        return true;
    }

    public LogEngine(string? customLogsDirectory = null)
    {
        _logsDirectory = ResolveLogsDirectory(customLogsDirectory);
        _channel = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _writerTask = Task.Run(ProcessQueueAsync);
    }

    private static string ResolveLogsDirectory(string? customDir)
    {
        if (!string.IsNullOrWhiteSpace(customDir))
        {
            try
            {
                Directory.CreateDirectory(customDir);
                return customDir;
            }
            catch
            {
                // Fallback below
            }
        }

        // Primary: App base directory / logs
        try
        {
            var baseLogs = BaseLogsDirectoryResolver != null
                ? BaseLogsDirectoryResolver()
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(baseLogs);
            // Quick test write check
            var testFile = Path.Combine(baseLogs, ".test_write");
            File.WriteAllText(testFile, string.Empty);
            File.Delete(testFile);
            return baseLogs;
        }
        catch
        {
            // Fallback: LocalAppData / RemoteManager / logs
            var appDataLogs = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RemoteManager", "logs");
            Directory.CreateDirectory(appDataLogs);
            return appDataLogs;
        }
    }

    public void Log(LogLevel level, string category, string message, Exception? ex = null)
    {
        var safeCategory = string.IsNullOrWhiteSpace(category) ? "General" : category;

        // Verify configuration and module enablement
        if (_settings != null)
        {
            if (!_settings.IsLoggingEnabled)
                return;

            if (!string.IsNullOrEmpty(_settings.MinimumLogLevel))
            {
                var min = ParseLogLevel(_settings.MinimumLogLevel);
                if (level < min)
                    return;
            }

            if (!IsModuleEnabled(safeCategory))
                return;
        }

        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Category = safeCategory,
            Message = message,
            Exception = ex,
            ExceptionDetails = ex?.ToString()
        };

        // Add to in-memory buffer
        lock (_bufferLock)
        {
            _recentBuffer.AddLast(entry);
            if (_recentBuffer.Count > MaxBufferSize)
            {
                _recentBuffer.RemoveFirst();
            }
        }

        // Queue for background disk write
        _channel.Writer.TryWrite(entry);

        // Notify real-time subscribers
        try
        {
            LogReceived?.Invoke(entry);
        }
        catch
        {
            // Subscribers should not crash logger
        }
    }

    public void Trace(string category, string message) => Log(LogLevel.Trace, category, message);
    public void Debug(string category, string message) => Log(LogLevel.Debug, category, message);
    public void Info(string category, string message) => Log(LogLevel.Info, category, message);
    public void Warn(string category, string message, Exception? ex = null) => Log(LogLevel.Warn, category, message, ex);
    public void Error(string category, string message, Exception? ex = null) => Log(LogLevel.Error, category, message, ex);
    public void Fatal(string category, string message, Exception? ex = null) => Log(LogLevel.Fatal, category, message, ex);

    public IReadOnlyList<LogEntry> GetRecentLogs(int count = 2000)
    {
        lock (_bufferLock)
        {
            if (count >= _recentBuffer.Count)
                return _recentBuffer.ToList();

            return _recentBuffer.TakeLast(count).ToList();
        }
    }

    public IReadOnlyList<string> GetAvailableLogFiles()
    {
        try
        {
            if (!Directory.Exists(_logsDirectory))
                return Array.Empty<string>();

            return Directory.GetFiles(_logsDirectory, "*.log")
                .OrderByDescending(f => f)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public async Task<IReadOnlyList<LogEntry>> ReadLogFileAsync(string filePath)
    {
        var result = new List<LogEntry>();
        if (!File.Exists(filePath))
            return result;

        try
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fileStream);

            string? line;
            LogEntry? currentEntry = null;
            // Matches: [2026-06-14 12:34:56.789] [INFO] [Category] Message
            var headerRegex = new Regex(@"^\[(?<time>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}(?:\.\d{3})?)\]\s+\[(?<level>[A-Z]+)\]\s+\[(?<cat>[^\]]+)\]\s+(?<msg>.*)$");

            while ((line = await reader.ReadLineAsync()) != null)
            {
                var match = headerRegex.Match(line);
                if (match.Success)
                {
                    if (currentEntry != null)
                    {
                        result.Add(currentEntry);
                    }

                    var timeStr = match.Groups["time"].Value;
                    var levelStr = match.Groups["level"].Value.Trim();
                    var category = match.Groups["cat"].Value;
                    var message = match.Groups["msg"].Value;

                    DateTime.TryParse(timeStr, out var timestamp);
                    var level = ParseLogLevel(levelStr);

                    currentEntry = new LogEntry
                    {
                        Timestamp = timestamp == default ? DateTime.Now : timestamp,
                        Level = level,
                        Category = category,
                        Message = message
                    };
                }
                else if (currentEntry != null && line.StartsWith("    Exception: "))
                {
                    currentEntry = new LogEntry
                    {
                        Timestamp = currentEntry.Timestamp,
                        Level = currentEntry.Level,
                        Category = currentEntry.Category,
                        Message = currentEntry.Message,
                        ExceptionDetails = line.Substring("    Exception: ".Length)
                    };
                }
                else if (currentEntry != null && !string.IsNullOrWhiteSpace(currentEntry.ExceptionDetails))
                {
                    currentEntry = new LogEntry
                    {
                        Timestamp = currentEntry.Timestamp,
                        Level = currentEntry.Level,
                        Category = currentEntry.Category,
                        Message = currentEntry.Message,
                        ExceptionDetails = currentEntry.ExceptionDetails + Environment.NewLine + line
                    };
                }
            }

            if (currentEntry != null)
            {
                result.Add(currentEntry);
            }
        }
        catch
        {
            // Ignore file read concurrency issues
        }

        return result;
    }

    private static LogLevel ParseLogLevel(string levelStr)
    {
        return levelStr switch
        {
            "TRACE" => LogLevel.Trace,
            "DEBUG" => LogLevel.Debug,
            "INFO"  => LogLevel.Info,
            "WARN"  => LogLevel.Warn,
            "ERROR" => LogLevel.Error,
            "FATAL" => LogLevel.Fatal,
            _ => LogLevel.Info
        };
    }

    private async Task ProcessQueueAsync()
    {
        var reader = _channel.Reader;

        try
        {
            while (await reader.WaitToReadAsync())
            {
                while (reader.TryRead(out var entry))
                {
                    await WriteToFileAsync(entry);
                }

                if (_currentWriter != null)
                {
                    await _currentWriter.FlushAsync();
                }
            }
        }
        finally
        {
            CloseCurrentWriter();
        }
    }

    private async Task WriteToFileAsync(LogEntry entry)
    {
        var dateString = entry.Timestamp.ToString("yyyy-MM-dd");

        if (_currentWriter == null || _currentDateString != dateString)
        {
            CloseCurrentWriter();

            var fileName = $"{dateString}.log";
            var filePath = Path.Combine(_logsDirectory, fileName);

            var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _currentWriter = new StreamWriter(stream) { AutoFlush = true };
            _currentDateString = dateString;
        }

        await _currentWriter.WriteLineAsync(entry.ToFileLogLine());
    }

    private void CloseCurrentWriter()
    {
        if (_currentWriter != null)
        {
            try
            {
                _currentWriter.Flush();
            }
            finally
            {
                _currentWriter.Dispose();
                _currentWriter = null;
                _currentDateString = null;
            }
        }
    }

    public async Task FlushAsync()
    {
        if (_currentWriter != null)
        {
            await _currentWriter.FlushAsync();
        }
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _writerTask.Wait(TimeSpan.FromSeconds(2));
        CloseCurrentWriter();
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        await _writerTask.WaitAsync(TimeSpan.FromSeconds(2));
        CloseCurrentWriter();
    }
}
