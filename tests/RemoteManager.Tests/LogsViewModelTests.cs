using System.Collections.ObjectModel;
using System.Diagnostics;
using RemoteManager.App.ViewModels;
using RemoteManager.Core.Logging;
using RemoteManager.Core.Models;
using Xunit;

namespace RemoteManager.Tests;

public class LogsViewModelTests
{
    private class FakeLogService : ILogService
    {
        public virtual string LogsDirectory => Path.GetTempPath();
        public AppSettings? CurrentSettings { get; set; }
        public event Action<LogEntry>? LogReceived;
        private readonly List<LogEntry> _logs = new();

        public void AddLog(LogEntry entry) => _logs.Add(entry);

        public void EmitLogReceived(LogEntry entry)
        {
            _logs.Add(entry);
            LogReceived?.Invoke(entry);
        }

        public void Configure(AppSettings settings) => CurrentSettings = settings;
        public void Log(LogLevel level, string category, string message, Exception? ex = null) { }
        public void Trace(string category, string message) { }
        public void Debug(string category, string message) { }
        public void Info(string category, string message) { }
        public void Warn(string category, string message, Exception? ex = null) { }
        public void Error(string category, string message, Exception? ex = null) { }
        public void Fatal(string category, string message, Exception? ex = null) { }

        public IReadOnlyList<LogEntry> GetRecentLogs(int count = 2000) => _logs.Take(count).ToList();
        public IReadOnlyList<string> GetAvailableLogFiles() => new List<string> { Path.Combine(LogsDirectory, "2026-09-12.log") };
        public Task<IReadOnlyList<LogEntry>> ReadLogFileAsync(string filePath) => Task.FromResult<IReadOnlyList<LogEntry>>(_logs);
        public Task FlushAsync() => Task.CompletedTask;
    }

    private static FakeLogService CreateSeededLogService()
    {
        var fake = new FakeLogService();
        fake.AddLog(new LogEntry { Level = LogLevel.Info, Category = "App", Message = "Application started" });
        fake.AddLog(new LogEntry { Level = LogLevel.Debug, Category = "Protocol.RDP", Message = "Connecting to RDP target 10.0.0.1" });
        fake.AddLog(new LogEntry { Level = LogLevel.Warn, Category = "Protocol.SSH", Message = "SSH host key not verified" });
        fake.AddLog(new LogEntry
        {
            Level = LogLevel.Error,
            Category = "Database",
            Message = "Failed to acquire database lock",
            Exception = new InvalidOperationException("Lock timeout"),
            ExceptionDetails = "System.InvalidOperationException: Lock timeout"
        });
        fake.AddLog(new LogEntry { Level = LogLevel.Info, Category = "UI", Message = "Navigation to Settings" });
        return fake;
    }

    [Fact]
    public void LogsViewModel_InitialLoad_LoadsLogsAndPopulatesCounts()
    {
        var fake = CreateSeededLogService();
        var vm = new LogsViewModel(fake);

        Assert.Equal(5, vm.TotalCount);
        Assert.Equal(2, vm.InfoCount); // "App", "UI"
        Assert.Equal(1, vm.WarningCount); // "Protocol.SSH"
        Assert.Equal(1, vm.ErrorCount); // "Database"
        Assert.Equal(5, vm.FilteredLogs.Count);
        Assert.Contains(LogsViewModel.LiveStreamTag, vm.AvailableFiles);
        Assert.True(vm.IsAutoScrollEnabled);
    }

    [Fact]
    public void LogsViewModel_FilterByLevel_FiltersCorrectly()
    {
        var fake = CreateSeededLogService();
        var vm = new LogsViewModel(fake);

        // Level: Error (should only show Error/Fatal)
        vm.SelectedLevel = "Error";
        Assert.Single(vm.FilteredLogs);
        Assert.Equal(LogLevel.Error, vm.FilteredLogs[0].Level);
        Assert.Equal("Database", vm.FilteredLogs[0].Category);

        // Level: Warning (should show Warning and above)
        vm.SelectedLevel = "Warning";
        Assert.Equal(2, vm.FilteredLogs.Count);
        Assert.Contains(vm.FilteredLogs, l => l.Level == LogLevel.Warn);
        Assert.Contains(vm.FilteredLogs, l => l.Level == LogLevel.Error);

        // Reset to All
        vm.SelectedLevel = "All";
        Assert.Equal(5, vm.FilteredLogs.Count);
    }

    [Fact]
    public void LogsViewModel_FilterByCategory_FiltersCorrectly()
    {
        var fake = CreateSeededLogService();
        var vm = new LogsViewModel(fake);

        // Filter by Protocol.RDP
        vm.SelectedCategory = "Protocol.RDP";
        Assert.Single(vm.FilteredLogs);
        Assert.Equal("Protocol.RDP", vm.FilteredLogs[0].Category);

        // Filter by Database
        vm.SelectedCategory = "Database";
        Assert.Single(vm.FilteredLogs);
        Assert.Equal("Database", vm.FilteredLogs[0].Category);

        // Reset to All Sources
        vm.SelectedCategory = "All Sources";
        Assert.Equal(5, vm.FilteredLogs.Count);
    }

    [Fact]
    public void LogsViewModel_SearchText_MatchesMessageCategoryOrException()
    {
        var fake = CreateSeededLogService();
        var vm = new LogsViewModel(fake);

        // Search by message content
        vm.SearchText = "host key";
        Assert.Single(vm.FilteredLogs);
        Assert.Equal("SSH host key not verified", vm.FilteredLogs[0].Message);

        // Search by exception detail
        vm.SearchText = "Lock timeout";
        Assert.Single(vm.FilteredLogs);
        Assert.Equal("Database", vm.FilteredLogs[0].Category);

        // Search non-existent
        vm.SearchText = "NonExistentKeywordXYZ";
        Assert.Empty(vm.FilteredLogs);
        Assert.Equal(0, vm.TotalCount);

        // Clear search
        vm.SearchText = "";
        Assert.Equal(5, vm.FilteredLogs.Count);
    }

    [Fact]
    public void LogsViewModel_ToggleAutoScroll_TogglesState()
    {
        var fake = CreateSeededLogService();
        var vm = new LogsViewModel(fake);

        Assert.True(vm.IsAutoScrollEnabled);
        vm.ToggleAutoScroll();
        Assert.False(vm.IsAutoScrollEnabled);
        vm.ToggleAutoScroll();
        Assert.True(vm.IsAutoScrollEnabled);
    }

    [Fact]
    public void LogsViewModel_ClearLogs_EmptiesLogsAndResetsCounts()
    {
        var fake = CreateSeededLogService();
        var vm = new LogsViewModel(fake);

        Assert.Equal(5, vm.TotalCount);
        vm.ClearLogs();

        Assert.Empty(vm.FilteredLogs);
        Assert.Equal(0, vm.TotalCount);
        Assert.Equal(0, vm.InfoCount);
        Assert.Equal(0, vm.WarningCount);
        Assert.Equal(0, vm.ErrorCount);
    }

    [Fact]
    public void LogsViewModel_Options_ProvideStandardFilters()
    {
        var fake = CreateSeededLogService();
        var vm = new LogsViewModel(fake);

        Assert.Contains("All", vm.LevelOptions);
        Assert.Contains("Info", vm.LevelOptions);
        Assert.Contains("Warning", vm.LevelOptions);
        Assert.Contains("Error", vm.LevelOptions);

        Assert.Contains("All Sources", vm.CategoryOptions);
        Assert.Contains("App", vm.CategoryOptions);
        Assert.Contains("Database", vm.CategoryOptions);
        Assert.Contains("Protocol.RDP", vm.CategoryOptions);
    }

    [Fact]
    public async Task LogsViewModel_FileSelection_LoadsHistoricalLogs()
    {
        var fake = CreateSeededLogService();
        var filePath = Path.Combine(fake.LogsDirectory, "2026-09-12.log");
        Directory.CreateDirectory(fake.LogsDirectory);
        await File.WriteAllTextAsync(filePath, "[2026-09-12 10:00:00.000] [INFO] [App] Historical log line" + Environment.NewLine);

        var vm = new LogsViewModel(fake);
        Assert.Contains("2026-09-12.log", vm.AvailableFiles);

        // Switch to historical file (triggers File.Exists branch)
        vm.SelectedFile = "2026-09-12.log";
        await Task.Delay(100);

        Assert.NotEmpty(vm.FilteredLogs);
        Assert.Contains("Loaded", vm.StatusNotification);

        // Switch back to Live Stream
        vm.SelectedFile = LogsViewModel.LiveStreamTag;
        await Task.Delay(100);
        Assert.NotEmpty(vm.FilteredLogs);
        Assert.Equal("Connected to live log stream.", vm.StatusNotification);
    }

    [Fact]
    public void LogsViewModel_CopyLogs_AllFilteredLogs_CopiesToClipboard()
    {
        string? copied = null;
        var prev = LogsViewModel.SetClipboardText;
        try
        {
            LogsViewModel.SetClipboardText = text => copied = text;
            var fake = CreateSeededLogService();
            var vm = new LogsViewModel(fake);
            vm.SelectedLog = null;
            Assert.NotEmpty(vm.FilteredLogs);

            vm.CopyLogs();
            Assert.NotNull(copied);
            Assert.Contains("Copied", vm.StatusNotification);
            Assert.Contains("log lines to clipboard", vm.StatusNotification);
        }
        finally
        {
            LogsViewModel.SetClipboardText = prev;
        }
    }

    [Fact]
    public void LogsViewModel_CopyLogs_EmptyView_ShowsNotification()
    {
        var fake = CreateSeededLogService();
        var vm = new LogsViewModel(fake);
        vm.ClearLogs();

        vm.CopyLogs();
        Assert.Equal("No logs available to copy.", vm.StatusNotification);
    }

    [Fact]
    public async Task LogsViewModel_ExportLogsAsync_EmptyLogs_ShowsNotification()
    {
        var fake = CreateSeededLogService();
        var vm = new LogsViewModel(fake);
        vm.ClearLogs();

        await vm.ExportLogsAsync();
        Assert.Equal("No logs available to export.", vm.StatusNotification);
    }

    [Fact]
    public void LogsViewModel_ToggleAutoScroll_TogglesAndNotifies()
    {
        var fake = CreateSeededLogService();
        var vm = new LogsViewModel(fake);
        var scrolled = false;
        vm.RequestScrollToEnd = () => scrolled = true;

        vm.IsAutoScrollEnabled = false;
        vm.ToggleAutoScroll();
        Assert.True(vm.IsAutoScrollEnabled);
        Assert.True(scrolled);
        Assert.Equal("Auto-scroll enabled.", vm.StatusNotification);

        vm.ToggleAutoScroll();
        Assert.False(vm.IsAutoScrollEnabled);
        Assert.Equal("Auto-scroll paused.", vm.StatusNotification);
    }

    [Fact]
    public void LogsViewModel_CopyLogs_SingleSelectedLog_CopiesSelected()
    {
        string? copied = null;
        var prev = LogsViewModel.SetClipboardText;
        try
        {
            LogsViewModel.SetClipboardText = text => copied = text;
            var fake = CreateSeededLogService();
            var vm = new LogsViewModel(fake);
            vm.SelectedLog = vm.FilteredLogs.First();

            vm.CopyLogs();
            Assert.NotNull(copied);
            Assert.Equal("Copied selected log entry to clipboard.", vm.StatusNotification);
        }
        finally
        {
            LogsViewModel.SetClipboardText = prev;
        }
    }

    [Fact]
    public void LogsViewModel_CopyLogs_ClipboardException_ShowsErrorNotification()
    {
        var prev = LogsViewModel.SetClipboardText;
        try
        {
            LogsViewModel.SetClipboardText = _ => throw new InvalidOperationException("Clipboard locked");
            var fake = CreateSeededLogService();
            var vm = new LogsViewModel(fake);
            vm.SelectedLog = vm.FilteredLogs.First();

            vm.CopyLogs();
            Assert.Equal("Failed to copy logs to clipboard.", vm.StatusNotification);
        }
        finally
        {
            LogsViewModel.SetClipboardText = prev;
        }
    }

    [Fact]
    public void LogsViewModel_FilterLevels_TraceAndDebugBranches()
    {
        var fake = CreateSeededLogService();
        fake.AddLog(new LogEntry { Level = LogLevel.Trace, Category = "System", Message = "Trace entry detail" });
        fake.AddLog(new LogEntry { Level = LogLevel.Debug, Category = "System", Message = "Debug entry detail" });
        var vm = new LogsViewModel(fake);

        vm.SelectedLevel = "Trace";
        Assert.All(vm.FilteredLogs, l => Assert.Equal(LogLevel.Trace, l.Level));

        vm.SelectedLevel = "Debug";
        Assert.All(vm.FilteredLogs, l => Assert.True(l.Level == LogLevel.Debug || l.Level == LogLevel.Trace));

        // Search text matching timestamp
        var timestamp = vm.FilteredLogs[0].FormattedTimestamp;
        vm.SearchText = timestamp;
        Assert.NotEmpty(vm.FilteredLogs);
    }

    [Fact]
    public void LogsViewModel_LiveStream_OnLogReceived_AppendsEntries()
    {
        var fake = CreateSeededLogService();
        var vm = new LogsViewModel(fake);
        Assert.Equal(LogsViewModel.LiveStreamTag, vm.SelectedFile);

        var countBefore = vm.FilteredLogs.Count;
        fake.EmitLogReceived(new LogEntry
        {
            Level = LogLevel.Info,
            Category = "App",
            Message = "Live dynamic streaming event"
        });

        Assert.Equal(countBefore + 1, vm.FilteredLogs.Count);
        Assert.Contains(vm.FilteredLogs, l => l.Message == "Live dynamic streaming event");
    }

    [Fact]
    public async Task LogsViewModel_ExportLogsAsync_WithSaveFilePathProvider_WritesFile()
    {
        var fake = CreateSeededLogService();
        var vm = new LogsViewModel(fake);
        var tempExportPath = Path.Combine(Path.GetTempPath(), $"TestExport_{Guid.NewGuid():N}.log");

        try
        {
            vm.SaveFilePathProvider = () => tempExportPath;
            await vm.ExportLogsAsync();

            Assert.True(File.Exists(tempExportPath));
            var content = await File.ReadAllTextAsync(tempExportPath);
            Assert.Contains("Application started", content);
            Assert.Contains("Logs successfully exported", vm.StatusNotification);
        }
        finally
        {
            if (File.Exists(tempExportPath)) File.Delete(tempExportPath);
        }
    }

    [Fact]
    public void LogsViewModel_OpenLogsFolder_ExecutesLauncher()
    {
        var fake = CreateSeededLogService();
        var vm = new LogsViewModel(fake);
        ProcessStartInfo? capturedPsi = null;

        var prevLauncher = LogsViewModel.ProcessLauncher;
        try
        {
            LogsViewModel.ProcessLauncher = psi =>
            {
                capturedPsi = psi;
                return null;
            };

            vm.OpenLogsFolder();
            Assert.NotNull(capturedPsi);
            Assert.Equal("explorer.exe", capturedPsi.FileName);
            Assert.Contains("Opened logs folder", vm.StatusNotification);
        }
        finally
        {
            LogsViewModel.ProcessLauncher = prevLauncher;
        }
    }

    [Fact]
    public void LogsViewModel_OnLogReceived_WhenNotLiveStream_IgnoresEntry()
    {
        var fakeService = new FakeLogService();
        var vm = new LogsViewModel(fakeService);
        vm.SelectedFile = "archive.log"; // Not Live Stream

        var initialCount = vm.FilteredLogs.Count;
        fakeService.EmitLogReceived(new LogEntry { Message = "Ignored log", Level = LogLevel.Info });
        Assert.Equal(initialCount, vm.FilteredLogs.Count);
    }

    [Fact]
    public void LogsViewModel_OpenLogsFolder_WhenDirectoryDoesNotExist_CreatesIt()
    {
        var nonExistentDir = Path.Combine(Path.GetTempPath(), $"NonExistentLogs_{Guid.NewGuid():N}");
        var fakeService = new CustomDirFakeLogService(nonExistentDir);
        var vm = new LogsViewModel(fakeService);

        var prevLauncher = LogsViewModel.ProcessLauncher;
        try
        {
            LogsViewModel.ProcessLauncher = psi => null;
            Assert.False(Directory.Exists(nonExistentDir));

            vm.OpenLogsFolder();
            Assert.True(Directory.Exists(nonExistentDir));
        }
        finally
        {
            LogsViewModel.ProcessLauncher = prevLauncher;
            if (Directory.Exists(nonExistentDir))
            {
                Directory.Delete(nonExistentDir, recursive: true);
            }
        }
    }

    [Fact]
    public void LogsViewModel_OpenLogsFolder_WhenLauncherThrows_ShowsNotification()
    {
        var fakeService = new FakeLogService();
        var vm = new LogsViewModel(fakeService);

        var prevLauncher = LogsViewModel.ProcessLauncher;
        try
        {
            LogsViewModel.ProcessLauncher = psi => throw new InvalidOperationException("Launch boom");
            vm.OpenLogsFolder();
            Assert.Contains("Failed to open logs folder", vm.StatusNotification);
        }
        finally
        {
            LogsViewModel.ProcessLauncher = prevLauncher;
        }
    }

    [Fact]
    public async Task LogsViewModel_ExportLogsAsync_WhenWriteFails_ShowsNotification()
    {
        var fakeService = new FakeLogService();
        var vm = new LogsViewModel(fakeService);
        fakeService.EmitLogReceived(new LogEntry { Message = "Export entry", Level = LogLevel.Info });

        vm.SaveFilePathProvider = () => @"Z:\NonExistentDirXYZ\Locked\Export.log";
        await vm.ExportLogsAsync();
        Assert.Contains("Failed to export logs", vm.StatusNotification);
    }

    [Fact]
    public void LogsViewModel_SafeDispatch_ExecutesDirectly()
    {
        var fake = new FakeLogService();
        var vm = new LogsViewModel(fake);
        bool called = false;
        vm.SafeDispatch(() => called = true);
        Assert.True(called);
    }

    private class CustomDirFakeLogService : FakeLogService
    {
        private readonly string _dir;
        public CustomDirFakeLogService(string dir) => _dir = dir;
        public override string LogsDirectory => _dir;
    }
}




