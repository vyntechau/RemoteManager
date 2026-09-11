using RemoteManager.Core.Logging;
using Xunit;

namespace RemoteManager.Tests;

public class LogEngineTests : IDisposable
{
    private readonly string _testLogsDir;

    public LogEngineTests()
    {
        _testLogsDir = Path.Combine(Path.GetTempPath(), $"RemoteManager_LogsTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testLogsDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testLogsDir))
            {
                Directory.Delete(_testLogsDir, true);
            }
        }
        catch { }
    }



    [Fact]
    public async Task Test_LogEngine_Creates_Daily_Log_File()
    {
        // Arrange
        using var engine = new LogEngine(_testLogsDir);
        var todayFileName = $"{DateTime.Now:yyyy-MM-dd}.log";
        var expectedFilePath = Path.Combine(_testLogsDir, todayFileName);

        // Act
        engine.Info("TestCategory", "This is an info log message.");
        engine.Warn("TestCategory", "This is a warning log message.");
        engine.Error("TestCategory", "This is an error log message.", new InvalidOperationException("Test ex"));

        // Wait a small moment for async queue writer
        await Task.Delay(300);
        await engine.FlushAsync();

        // Assert
        Assert.True(File.Exists(expectedFilePath), $"Expected file {expectedFilePath} to exist.");

        using var fs = new FileStream(expectedFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        var content = await sr.ReadToEndAsync();
        Assert.Contains("[INFO] [TestCategory] This is an info log message.", content);
        Assert.Contains("[WARN] [TestCategory] This is a warning log message.", content);
        Assert.Contains("[ERROR] [TestCategory] This is an error log message.", content);
        Assert.Contains("InvalidOperationException", content);
    }

    [Fact]
    public void Test_LogEngine_Recent_Buffer_And_Event()
    {
        // Arrange
        using var engine = new LogEngine(_testLogsDir);
        LogEntry? receivedEvent = null;
        engine.LogReceived += entry => receivedEvent = entry;

        // Act
        engine.Info("BufferCategory", "Buffered message 123");

        // Assert
        Assert.NotNull(receivedEvent);
        Assert.Equal("Buffered message 123", receivedEvent.Message);
        Assert.Equal("BufferCategory", receivedEvent.Category);
        Assert.Equal(LogLevel.Info, receivedEvent.Level);

        var recent = engine.GetRecentLogs();
        Assert.Contains(recent, e => e.Message == "Buffered message 123");
    }

    [Fact]
    public async Task Test_LogEngine_Read_Log_File()
    {
        // Arrange
        using var engine = new LogEngine(_testLogsDir);
        engine.Info("App", "Application started successfully");
        engine.Warn("Security", "Token expiration approaching");

        await Task.Delay(300);
        await engine.FlushAsync();

        var todayFileName = $"{DateTime.Now:yyyy-MM-dd}.log";
        var logFilePath = Path.Combine(_testLogsDir, todayFileName);

        // Act
        var parsedEntries = await engine.ReadLogFileAsync(logFilePath);

        // Assert
        Assert.NotEmpty(parsedEntries);
        Assert.Contains(parsedEntries, e => e.Message == "Application started successfully" && e.Level == LogLevel.Info);
        Assert.Contains(parsedEntries, e => e.Message == "Token expiration approaching" && e.Level == LogLevel.Warn);
    }

    [Fact]
    public async Task Test_LogEngine_Daily_File_Pattern_And_Listing()
    {
        // Arrange
        using var engine = new LogEngine(_testLogsDir);
        engine.Info("Test", "Daily file pattern validation entry");
        await Task.Delay(300);
        await engine.FlushAsync();

        // Act
        var availableFiles = engine.GetAvailableLogFiles();

        // Assert
        Assert.NotEmpty(availableFiles);
        var expectedFile = Path.Combine(_testLogsDir, $"{DateTime.Now:yyyy-MM-dd}.log");
        Assert.Contains(availableFiles, f => string.Equals(f, expectedFile, StringComparison.OrdinalIgnoreCase));

        // Verify the file name format specifically matches yyyy-MM-dd.log (e.g. 2026-06-14.log)
        var fileName = Path.GetFileName(availableFiles[0]);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}\.log$", fileName);
    }

    [Fact]
    public void Test_LogEngine_Module_And_Master_Filtering()
    {
        // Arrange
        using var engine = new LogEngine(_testLogsDir);
        var settings = new RemoteManager.Core.Models.AppSettings
        {
            IsLoggingEnabled = true,
            LogRdp = true,
            LogSsh = false, // Disabled module
            LogDatabase = true,
            MinimumLogLevel = "Info"
        };
        engine.Configure(settings);

        var emitted = new List<LogEntry>();
        engine.LogReceived += entry => emitted.Add(entry);

        // Act
        engine.Debug("Protocol.RDP", "Filtered out because below minimum level Info");
        engine.Info("Protocol.RDP", "RDP info message");
        engine.Info("Protocol.SSH", "SSH info message - should be filtered because LogSsh is false");
        engine.Info("Database", "Database info message");

        // Assert
        Assert.Contains(emitted, e => e.Message == "RDP info message");
        Assert.Contains(emitted, e => e.Message == "Database info message");
        Assert.DoesNotContain(emitted, e => e.Message == "SSH info message - should be filtered because LogSsh is false");
        Assert.DoesNotContain(emitted, e => e.Message == "Filtered out because below minimum level Info");

        // Test master disable
        settings.IsLoggingEnabled = false;
        engine.Info("Protocol.RDP", "Should be ignored because master logging is disabled");
        Assert.DoesNotContain(emitted, e => e.Message == "Should be ignored because master logging is disabled");
    }
}
