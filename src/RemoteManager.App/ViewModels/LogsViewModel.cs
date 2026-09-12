using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RemoteManager.Core.Logging;

namespace RemoteManager.App.ViewModels;

public partial class LogsViewModel : ObservableObject
{
    private readonly ILogService _logService;
    private readonly List<LogEntry> _allEntries = [];
    private readonly object _lock = new();

    public const string LiveStreamTag = "Live Stream (Today)";

    [ObservableProperty]
    private ObservableCollection<LogEntry> _filteredLogs = [];

    [ObservableProperty]
    private LogEntry? _selectedLog;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedLevel = "All";

    [ObservableProperty]
    private string _selectedCategory = "All Sources";

    [ObservableProperty]
    private ObservableCollection<string> _availableFiles = [];

    [ObservableProperty]
    private string _selectedFile = LiveStreamTag;

    [ObservableProperty]
    private bool _isAutoScrollEnabled = true;

    [ObservableProperty]
    private string _statusNotification = string.Empty;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _infoCount;

    [ObservableProperty]
    private int _warningCount;

    [ObservableProperty]
    private int _errorCount;

    public Action? RequestScrollToEnd { get; set; }

    public IReadOnlyList<string> LevelOptions { get; } =
    [
        "All",
        "Trace",
        "Debug",
        "Info",
        "Warning",
        "Error"
    ];

    public IReadOnlyList<string> CategoryOptions { get; } =
    [
        "All Sources",
        "App",
        "Database",
        "Protocol.RDP",
        "Protocol.SSH",
        "Protocol.VNC",
        "Protocol.Web",
        "Session",
        "Security",
        "UI"
    ];

    public LogsViewModel(ILogService? logService = null)
    {
        _logService = logService ?? LogEngine.Instance;

        LoadInitialLogs();
        RefreshAvailableFiles();

        _logService.LogReceived += OnLogReceived;
    }

    public void RefreshAvailableFiles()
    {
        var files = _logService.GetAvailableLogFiles();
        var list = new List<string> { LiveStreamTag };

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            if (!list.Contains(fileName))
            {
                list.Add(fileName);
            }
        }

        AvailableFiles = new ObservableCollection<string>(list);
    }

    private void LoadInitialLogs()
    {
        lock (_lock)
        {
            _allEntries.Clear();
            _allEntries.AddRange(_logService.GetRecentLogs(2000));
        }

        ApplyFilters();
    }

    private void OnLogReceived(LogEntry entry)
    {
        // Only append to active view if user is looking at Live Stream
        if (SelectedFile != LiveStreamTag)
            return;

        Action action = () =>
        {
            lock (_lock)
            {
                _allEntries.Add(entry);
                if (_allEntries.Count > 5000)
                {
                    _allEntries.RemoveAt(0);
                }
            }

            if (MatchesFilter(entry))
            {
                FilteredLogs.Add(entry);
                UpdateCounts();

                if (IsAutoScrollEnabled)
                {
                    RequestScrollToEnd?.Invoke();
                }
            }
        };

        SafeDispatch(action);
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnSelectedLevelChanged(string value) => ApplyFilters();
    partial void OnSelectedCategoryChanged(string value) => ApplyFilters();

    partial void OnSelectedFileChanged(string value)
    {
        _ = HandleFileSelectionChangeAsync(value);
    }

    private async Task HandleFileSelectionChangeAsync(string fileName)
    {
        if (fileName == LiveStreamTag)
        {
            LoadInitialLogs();
            ShowNotification("Connected to live log stream.");
            return;
        }

        var fullPath = Path.Combine(_logService.LogsDirectory, fileName);
        if (File.Exists(fullPath))
        {
            ShowNotification($"Loading logs from {fileName}...");
            var loaded = await _logService.ReadLogFileAsync(fullPath);
            lock (_lock)
            {
                _allEntries.Clear();
                _allEntries.AddRange(loaded);
            }
            ApplyFilters();
            ShowNotification($"Loaded {loaded.Count} entries from {fileName}.");
        }
    }

    private void ApplyFilters()
    {
        lock (_lock)
        {
            var query = _allEntries.Where(MatchesFilter).ToList();
            FilteredLogs = new ObservableCollection<LogEntry>(query);
        }

        UpdateCounts();

        if (IsAutoScrollEnabled)
        {
            RequestScrollToEnd?.Invoke();
        }
    }

    private bool MatchesFilter(LogEntry entry)
    {
        // Level filter
        if (SelectedLevel != "All")
        {
            if (SelectedLevel == "Trace" && entry.Level != LogLevel.Trace) return false;
            if (SelectedLevel == "Debug" && entry.Level != LogLevel.Debug && entry.Level != LogLevel.Trace) return false;
            if (SelectedLevel == "Info" && entry.Level < LogLevel.Info) return false;
            if (SelectedLevel == "Warning" && entry.Level != LogLevel.Warn && entry.Level != LogLevel.Error && entry.Level != LogLevel.Fatal) return false;
            if (SelectedLevel == "Error" && entry.Level != LogLevel.Error && entry.Level != LogLevel.Fatal) return false;
        }

        // Category filter
        if (SelectedCategory != "All Sources")
        {
            if (!entry.Category.StartsWith(SelectedCategory, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Search text
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var query = SearchText.Trim();
            var matchesMsg = entry.Message.Contains(query, StringComparison.OrdinalIgnoreCase);
            var matchesCat = entry.Category.Contains(query, StringComparison.OrdinalIgnoreCase);
            var matchesTime = entry.FormattedTimestamp.Contains(query, StringComparison.OrdinalIgnoreCase);
            var matchesEx = entry.ExceptionDetails != null && entry.ExceptionDetails.Contains(query, StringComparison.OrdinalIgnoreCase);

            if (!matchesMsg && !matchesCat && !matchesTime && !matchesEx)
                return false;
        }

        return true;
    }

    private void UpdateCounts()
    {
        TotalCount = FilteredLogs.Count;
        InfoCount = FilteredLogs.Count(e => e.Level == LogLevel.Info);
        WarningCount = FilteredLogs.Count(e => e.Level == LogLevel.Warn);
        ErrorCount = FilteredLogs.Count(e => e.Level == LogLevel.Error || e.Level == LogLevel.Fatal);
    }

    [RelayCommand]
    public void ToggleAutoScroll()
    {
        IsAutoScrollEnabled = !IsAutoScrollEnabled;
        if (IsAutoScrollEnabled)
        {
            RequestScrollToEnd?.Invoke();
            ShowNotification("Auto-scroll enabled.");
        }
        else
        {
            ShowNotification("Auto-scroll paused.");
        }
    }

    [RelayCommand]
    public void CopyLogs()
    {
        if (SelectedLog != null)
        {
            Clipboard.SetText(SelectedLog.ToFileLogLine());
            ShowNotification("Copied selected log entry to clipboard.");
            return;
        }

        if (FilteredLogs.Count == 0)
        {
            ShowNotification("No logs available to copy.");
            return;
        }

        var lines = string.Join(Environment.NewLine, FilteredLogs.Select(e => e.ToFileLogLine()));
        Clipboard.SetText(lines);
        ShowNotification($"Copied {FilteredLogs.Count} log lines to clipboard.");
    }

    [RelayCommand]
    public void ClearLogs()
    {
        lock (_lock)
        {
            _allEntries.Clear();
        }
        FilteredLogs.Clear();
        UpdateCounts();
        ShowNotification("View cleared.");
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static string? DefaultSaveFilePathProvider()
    {
        var sfd = new SaveFileDialog
        {
            Title = "Export Log Entries",
            Filter = "Log File (*.log)|*.log|Text File (*.txt)|*.txt|All Files (*.*)|*.*",
            FileName = $"ExportedLogs_{DateTime.Now:yyyy-MM-dd_HHmmss}.log"
        };
        return sfd.ShowDialog() == true ? sfd.FileName : null;
    }

    internal Func<string?> SaveFilePathProvider { get; set; } = DefaultSaveFilePathProvider;
    internal static Func<ProcessStartInfo, Process?> ProcessLauncher { get; set; } = psi => Process.Start(psi);
    internal Action<Action> SafeDispatch { get; set; } = action =>
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(action);
        }
        else
        {
            action();
        }
    };

    [RelayCommand]
    public void OpenLogsFolder()
    {
        try
        {
            var dir = _logService.LogsDirectory;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            ProcessLauncher(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{dir}\"",
                UseShellExecute = true
            });
            ShowNotification($"Opened logs folder: {dir}");
        }
        catch (Exception ex)
        {
            ShowNotification($"Failed to open logs folder: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task ExportLogsAsync()
    {
        if (FilteredLogs.Count == 0)
        {
            ShowNotification("No logs available to export.");
            return;
        }

        var targetPath = SaveFilePathProvider();
        if (string.IsNullOrEmpty(targetPath)) return;

        if (!string.IsNullOrEmpty(targetPath))
        {
            try
            {
                var content = string.Join(Environment.NewLine, FilteredLogs.Select(e => e.ToFileLogLine()));
                await File.WriteAllTextAsync(targetPath, content);
                ShowNotification($"Logs successfully exported to {Path.GetFileName(targetPath)}.");
            }
            catch (Exception ex)
            {
                ShowNotification($"Failed to export logs: {ex.Message}");
            }
        }
    }

    private void ShowNotification(string message)
    {
        StatusNotification = message;
        _ = Task.Run(async () =>
        {
            await Task.Delay(3500);
            if (StatusNotification == message)
            {
                SafeDispatch(() => StatusNotification = string.Empty);
            }
        });
    }
}
