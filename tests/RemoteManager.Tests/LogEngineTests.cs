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

    [Fact]
    public async Task Test_LogEngine_Trace_Fatal_And_DisposeAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"LogTest_Trace_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var engine = new LogEngine(tempDir);
            engine.Trace("TraceCat", "Trace level entry");
            engine.Fatal("FatalCat", "Fatal system error", new ApplicationException("Fatal ex"));

            await Task.Delay(100);
            await engine.FlushAsync();
            await engine.DisposeAsync();

            var logFile = Directory.GetFiles(tempDir, "*.log").FirstOrDefault();
            Assert.NotNull(logFile);
            var content = await File.ReadAllTextAsync(logFile);
            Assert.Contains("Trace level entry", content);
            Assert.Contains("Fatal system error", content);
            Assert.Contains("ApplicationException: Fatal ex", content);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Test_LogEngine_ReadLogFile_NonExistentAndMalformedLines()
    {
        using var engine = new LogEngine(_testLogsDir);

        // Non-existent file returns empty
        var nonExistent = await engine.ReadLogFileAsync(Path.Combine(_testLogsDir, "does_not_exist.log"));
        Assert.Empty(nonExistent);

        // Write a log file with malformed and multi-line exception entries
        var customLogFile = Path.Combine(_testLogsDir, "custom_test.log");
        var logContent = @"[2026-09-12 10:00:00.000] [INFO] [System] Normal line
Malformed non-matching line that gets ignored
    Exception: System.Exception: Multiline stack trace line
[2026-09-12 10:01:00.000] [ERROR] [Network] Socket failed
    Exception: System.Net.Sockets.SocketException: Connection refused";
        await File.WriteAllTextAsync(customLogFile, logContent);

        var entries = await engine.ReadLogFileAsync(customLogFile);
        Assert.True(entries.Count >= 2);
        Assert.Equal("Normal line", entries[0].Message);
        Assert.Equal("Socket failed", entries[1].Message);
        Assert.True(entries[1].HasException);
    }

    [Fact]
    public void Test_LogEngine_IsModuleEnabled_CoversAllCategories()
    {
        using var engine = new LogEngine(_testLogsDir);
        var settings = new RemoteManager.Core.Models.AppSettings
        {
            IsLoggingEnabled = true,
            LogRdp = true,
            LogSsh = false,
            LogVnc = true,
            LogWeb = false,
            LogDatabase = true,
            LogSecurity = false,
            LogAppLifecycle = true
        };
        engine.Configure(settings);

        Assert.True(engine.IsModuleEnabled("Protocol.RDP"));
        Assert.False(engine.IsModuleEnabled("Protocol.SSH"));
        Assert.True(engine.IsModuleEnabled("Protocol.VNC"));
        Assert.False(engine.IsModuleEnabled("Protocol.Web"));
        Assert.True(engine.IsModuleEnabled("Database"));
        Assert.False(engine.IsModuleEnabled("Security"));
        Assert.True(engine.IsModuleEnabled("App"));
        Assert.True(engine.IsModuleEnabled("UI"));
        Assert.True(engine.IsModuleEnabled("Session"));
        Assert.True(engine.IsModuleEnabled("UnknownModule"));
    }

    [Fact]
    public void Test_LogEngine_SubscriberException_DoesNotCrashEngine()
    {
        using var engine = new LogEngine(_testLogsDir);
        engine.LogReceived += _ => throw new InvalidOperationException("Subscriber failure");

        // Should not throw
        engine.Info("Test", "This message should be processed safely even if subscriber throws");
    }

    [Fact]
    public async Task Test_LogEngine_RemainingEdgeCases()
    {
        using var engine = new LogEngine(_testLogsDir);

        // 1. Properties
        Assert.Equal(_testLogsDir, engine.LogsDirectory);
        Assert.Null(engine.CurrentSettings);

        // 2. Buffer take count when count < total entries
        for (int i = 0; i < 15; i++)
        {
            engine.Info("Cat", $"Message {i}");
        }
        var recent5 = engine.GetRecentLogs(5);
        Assert.Equal(5, recent5.Count);

        // 3. GetAvailableLogFiles on empty directory
        var emptyLogsDir = Path.Combine(Path.GetTempPath(), $"EmptyLogs_{Guid.NewGuid():N}");
        using var emptyEngine = new LogEngine(emptyLogsDir);
        var files = emptyEngine.GetAvailableLogFiles();
        Assert.Empty(files);

        // 4. ReadLogFileAsync with TRACE, DEBUG, FATAL and multiline exception continuation
        var testLogFile = Path.Combine(_testLogsDir, "MultiLineTest.log");
        var lines = new[]
        {
            "[2026-06-14 12:00:00.000] [TRACE] [Cat] Trace line",
            "[2026-06-14 12:00:01.000] [DEBUG] [Cat] Debug line",
            "[2026-06-14 12:00:02.000] [FATAL] [Cat] Fatal line",
            "    Exception: Primary exception info",
            "    at SomeNamespace.SomeMethod() in file.cs:line 10",
            "    at AnotherMethod() in file.cs:line 20"
        };
        await File.WriteAllLinesAsync(testLogFile, lines);

        var readEntries = await engine.ReadLogFileAsync(testLogFile);
        Assert.Equal(3, readEntries.Count);
        Assert.Equal(LogLevel.Trace, readEntries[0].Level);
        Assert.Equal(LogLevel.Debug, readEntries[1].Level);
        Assert.Equal(LogLevel.Fatal, readEntries[2].Level);
        Assert.Contains("at SomeNamespace.SomeMethod()", readEntries[2].ExceptionDetails);
        Assert.Contains("at AnotherMethod()", readEntries[2].ExceptionDetails);
    }

    [Fact]
    public async Task Test_LogEngine_MaxBufferSize_EvictsOldest()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"LogBufferTest_{Guid.NewGuid():N}");
        using var engine = new LogEngine(tempDir);

        for (int i = 0; i < 2005; i++)
        {
            engine.Info("Stress", $"Msg {i}");
        }

        var recent = engine.GetRecentLogs(2500);
        Assert.True(recent.Count <= 2000);
    }

    [Fact]
    public void Test_LogEngine_InvalidCustomDir_FallsBack()
    {
        // Invalid path with characters that cannot be created on Windows
        using var engine = new LogEngine("Z:\\\0invalid\\logs");
        Assert.NotNull(engine.LogsDirectory);
        Assert.True(Directory.Exists(engine.LogsDirectory));
    }

    [Fact]
    public async Task Test_LogEngine_FlushAsync_And_DisposeAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"LogFlushTest_{Guid.NewGuid():N}");
        var engine = new LogEngine(tempDir);

        engine.Info("Test", "Flush entry");
        // Give background queue moment to initialize writer
        await Task.Delay(100);
        await engine.FlushAsync();

        await engine.DisposeAsync();
    }

    [Fact]
    public async Task Test_LogEngine_ReadLogFileAsync_LockedFile_CatchesGracefully()
    {
        var testFile = Path.Combine(_testLogsDir, $"LockedRead_{Guid.NewGuid():N}.log");
        await File.WriteAllTextAsync(testFile, "test line");

        using var engine = new LogEngine(_testLogsDir);
        using (var fs = new FileStream(testFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var entries = await engine.ReadLogFileAsync(testFile);
            Assert.Empty(entries);
        }
    }

    [Fact]
    public void Test_LogEngine_BaseLogsDirectory_Unwritable_FallsBackToLocalAppData()
    {
        var prev = LogEngine.BaseLogsDirectoryResolver;
        try
        {
            LogEngine.BaseLogsDirectoryResolver = () => @"Z:\UnwritablePathXYZ\logs";
            using var engine = new LogEngine();
            Assert.NotNull(engine.LogsDirectory);
            Assert.Contains("RemoteManager", engine.LogsDirectory);
            Assert.Contains("logs", engine.LogsDirectory);
        }
        finally
        {
            LogEngine.BaseLogsDirectoryResolver = prev;
        }
    }

    [Fact]
    public void Test_LogEngine_GetAvailableLogFiles_WhenDirectoryDeleted_ReturnsEmpty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"DeletedDir_{Guid.NewGuid():N}");
        using var engine = new LogEngine(tempDir);
        // Delete directory after initialization
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
        var files = engine.GetAvailableLogFiles();
        Assert.Empty(files);
    }

    [Fact]
    public void Test_LogEngine_GetAvailableLogFiles_ExceptionHandling()
    {
        using var engine = new LogEngine();
        if (Directory.Exists(@"C:\System Volume Information"))
        {
            engine.LogsDirectory = @"C:\System Volume Information";
            var files = engine.GetAvailableLogFiles();
            Assert.Empty(files);
        }
    }

    [Fact]
    public async Task Test_LogEngine_ShutdownDrainRemainingMessages()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"LogTest_Drain_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var engine = new LogEngine(tempDir);
            for (int i = 0; i < 40; i++)
            {
                engine.Info("DrainCat", $"Rapid message #{i}");
            }
            await engine.DisposeAsync();

            var logFiles = engine.GetAvailableLogFiles();
            Assert.NotEmpty(logFiles);
            var content = await File.ReadAllTextAsync(logFiles[0]);
            Assert.Contains("Rapid message #0", content);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Test_LogEngine_SyncDispose()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"LogTest_Sync_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var engine = new LogEngine(tempDir);
            engine.Info("SyncCat", "Sync dispose entry");
            engine.Dispose();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}


