using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteManager.Core.Logging;
using RemoteManager.Core.Models;
using Wpf.Ui.Controls;

namespace RemoteManager.App.ViewModels;

public partial class ConnectionsViewModel : ObservableObject
{
    private readonly MainViewModel _mainViewModel;
    private readonly List<ConnectionCardViewModel> _allCards = [];

    [ObservableProperty]
    private ObservableCollection<ConnectionCardViewModel> _filteredCards = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    private bool _suppressFilterChangeEvents;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    [NotifyPropertyChangedFor(nameof(ActiveFilterSummary))]
    [NotifyPropertyChangedFor(nameof(ActiveFilterBadgeText))]
    private bool _isFilterAll = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    [NotifyPropertyChangedFor(nameof(ActiveFilterSummary))]
    [NotifyPropertyChangedFor(nameof(ActiveFilterBadgeText))]
    private bool _isFilterBookmarked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    [NotifyPropertyChangedFor(nameof(ActiveFilterSummary))]
    [NotifyPropertyChangedFor(nameof(ActiveFilterBadgeText))]
    private bool _isFilterRdp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    [NotifyPropertyChangedFor(nameof(ActiveFilterSummary))]
    [NotifyPropertyChangedFor(nameof(ActiveFilterBadgeText))]
    private bool _isFilterSsh;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    [NotifyPropertyChangedFor(nameof(ActiveFilterSummary))]
    [NotifyPropertyChangedFor(nameof(ActiveFilterBadgeText))]
    private bool _isFilterVnc;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    [NotifyPropertyChangedFor(nameof(ActiveFilterSummary))]
    [NotifyPropertyChangedFor(nameof(ActiveFilterBadgeText))]
    private bool _isFilterWeb;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    [NotifyPropertyChangedFor(nameof(ActiveFilterSummary))]
    [NotifyPropertyChangedFor(nameof(ActiveFilterBadgeText))]
    private bool _isFilterOnlineOnly;

    [ObservableProperty]
    private string _selectedProtocolFilter = "All";

    public bool HasActiveFilter => !IsFilterAll;

    public string ActiveFilterBadgeText
    {
        get
        {
            if (IsFilterAll) return string.Empty;
            int count = 0;
            if (IsFilterBookmarked) count++;
            if (IsFilterRdp) count++;
            if (IsFilterSsh) count++;
            if (IsFilterVnc) count++;
            if (IsFilterWeb) count++;
            if (IsFilterOnlineOnly) count++;
            return count > 0 ? count.ToString() : string.Empty;
        }
    }

    public string ActiveFilterSummary
    {
        get
        {
            if (IsFilterAll) return $"All Servers ({TotalCount})";

            var active = new List<string>();
            if (IsFilterBookmarked) active.Add("Bookmarked");
            if (IsFilterRdp) active.Add("RDP");
            if (IsFilterSsh) active.Add("SSH");
            if (IsFilterVnc) active.Add("VNC");
            if (IsFilterWeb) active.Add("Web");
            if (IsFilterOnlineOnly) active.Add("Online");

            if (active.Count == 0) return $"All Servers ({TotalCount})";
            if (active.Count <= 2) return $"{string.Join(", ", active)} ({FilteredCards.Count})";
            return $"Filters ({active.Count}) • {FilteredCards.Count}";
        }
    }

    private bool _suppressSortChangeEvents;

    [ObservableProperty]
    private string _selectedSortOption = "Custom Order";

    [ObservableProperty]
    private string _currentSortColumn = string.Empty;

    [ObservableProperty]
    private bool _isSortAscending = true;

    private static readonly SolidColorBrush ActiveSortBrush = new(Color.FromRgb(0x60, 0xCD, 0xFF));
    private static readonly SolidColorBrush InactiveSortBrush = new(Color.FromArgb(120, 255, 255, 255));

    public SymbolRegular GetColumnSortIcon(string columnKey)
    {
        if (string.Equals(CurrentSortColumn, columnKey, StringComparison.OrdinalIgnoreCase))
        {
            return IsSortAscending ? SymbolRegular.ChevronUp16 : SymbolRegular.ChevronDown16;
        }
        return SymbolRegular.ChevronUpDown16;
    }

    public Brush GetColumnSortIconBrush(string columnKey)
    {
        if (string.Equals(CurrentSortColumn, columnKey, StringComparison.OrdinalIgnoreCase))
        {
            return (Application.Current?.TryFindResource("AccentTextFillColorPrimaryBrush") as Brush) ?? ActiveSortBrush;
        }
        return (Application.Current?.TryFindResource("TextFillColorTertiaryBrush") as Brush) ?? InactiveSortBrush;
    }

    public SymbolRegular BookmarkSortIcon => GetColumnSortIcon("Bookmark");
    public Brush BookmarkSortIconBrush => GetColumnSortIconBrush("Bookmark");

    public SymbolRegular StatusSortIcon => GetColumnSortIcon("Status");
    public Brush StatusSortIconBrush => GetColumnSortIconBrush("Status");

    public SymbolRegular ProtocolSortIcon => GetColumnSortIcon("Protocol");
    public Brush ProtocolSortIconBrush => GetColumnSortIconBrush("Protocol");

    public SymbolRegular NameSortIcon => GetColumnSortIcon("DisplayName");
    public Brush NameSortIconBrush => GetColumnSortIconBrush("DisplayName");

    public SymbolRegular EndpointSortIcon => GetColumnSortIcon("Endpoint");
    public Brush EndpointSortIconBrush => GetColumnSortIconBrush("Endpoint");

    public SymbolRegular CredentialSortIcon => GetColumnSortIcon("Credential");
    public Brush CredentialSortIconBrush => GetColumnSortIconBrush("Credential");

    public SymbolRegular DisplayModeSortIcon => GetColumnSortIcon("DisplayMode");
    public Brush DisplayModeSortIconBrush => GetColumnSortIconBrush("DisplayMode");

    private string GetHeaderWithCaret(string label, string columnKey)
    {
        if (string.Equals(CurrentSortColumn, columnKey, StringComparison.OrdinalIgnoreCase))
        {
            return $"{label} {(IsSortAscending ? "^" : "v")}";
        }
        return $"{label} ^v";
    }

    public string BookmarkHeader => GetHeaderWithCaret("★", "Bookmark");
    public string StatusHeader => GetHeaderWithCaret("Status", "Status");
    public string ProtocolHeader => GetHeaderWithCaret("Protocol", "Protocol");
    public string NameHeader => GetHeaderWithCaret("Server Name", "DisplayName");
    public string EndpointHeader => GetHeaderWithCaret("Endpoint", "Endpoint");
    public string CredentialHeader => GetHeaderWithCaret("Credential Vault", "Credential");
    public string DisplayModeHeader => GetHeaderWithCaret("Display Mode", "DisplayMode");

    [ObservableProperty]
    private bool _isGridView = true;

    public bool IsTableView => !IsGridView;

    // Quick Connect drawer / inline bar on Connection page
    [ObservableProperty]
    private bool _isQuickConnectExpanded = false;

    [ObservableProperty]
    private string _quickHost = string.Empty;

    [ObservableProperty]
    private ProtocolType _quickProtocol = ProtocolType.RDP;

    // KPI / Count badges
    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _bookmarkedCount;

    [ObservableProperty]
    private int _rdpCount;

    [ObservableProperty]
    private int _sshCount;

    [ObservableProperty]
    private int _vncCount;

    [ObservableProperty]
    private int _webCount;

    [ObservableProperty]
    private int _onlineCount;

    [ObservableProperty]
    private bool _isPingingAll;

    public List<string> SortOptions { get; } =
    [
        "Custom Order",
        "Name (A-Z)",
        "Name (Z-A)",
        "Protocol",
        "Host / IP",
        "Port"
    ];

    public ConnectionsViewModel(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
    }

    public void SyncConnections(IEnumerable<ConnectionItem> connections, IEnumerable<Credential> credentials)
    {
        var credDict = credentials.ToDictionary(c => c.Id, c => c.Title);

        _allCards.Clear();
        foreach (var conn in connections)
        {
            var credTitle = conn.CredentialId.HasValue && credDict.TryGetValue(conn.CredentialId.Value, out var title)
                ? title
                : "(None / Prompt)";

            var card = new ConnectionCardViewModel(conn, credTitle);
            card.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ConnectionCardViewModel.PingStatus))
                {
                    UpdateMetrics();
                }
            };
            _allCards.Add(card);
        }

        UpdateMetrics();
        ApplyFilterAndSort();
    }

    private void UpdateMetrics()
    {
        TotalCount = _allCards.Count;
        BookmarkedCount = _allCards.Count(c => c.IsBookmarked);
        RdpCount = _allCards.Count(c => c.Protocol == ProtocolType.RDP);
        SshCount = _allCards.Count(c => c.Protocol == ProtocolType.SSH);
        VncCount = _allCards.Count(c => c.Protocol == ProtocolType.VNC);
        WebCount = _allCards.Count(c => c.Protocol == ProtocolType.Web);
        OnlineCount = _allCards.Count(c => c.PingStatus == ConnectionPingStatus.Online);
        OnPropertyChanged(nameof(ActiveFilterSummary));
    }

    public void MoveCardToTop(Guid connectionId)
    {
        var card = _allCards.FirstOrDefault(c => c.Model.Id == connectionId);
        if (card != null)
        {
            var idx = _allCards.IndexOf(card);
            if (idx > 0)
            {
                int targetIdx = card.IsBookmarked ? 0 : _allCards.Take(idx).Count(c => c.IsBookmarked);
                if (idx != targetIdx)
                {
                    _allCards.RemoveAt(idx);
                    _allCards.Insert(targetIdx, card);
                }
            }

            var fIdx = FilteredCards.IndexOf(card);
            if (fIdx > 0)
            {
                int targetFIdx = card.IsBookmarked ? 0 : FilteredCards.Take(fIdx).Count(c => c.IsBookmarked);
                if (fIdx != targetFIdx)
                {
                    FilteredCards.Move(fIdx, targetFIdx);
                }
            }
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        if (_mainViewModel != null && _mainViewModel.SearchText != value)
        {
            _mainViewModel.SearchText = value;
        }
        ApplyFilterAndSort();
    }
    partial void OnSelectedSortOptionChanged(string value)
    {
        if (!_suppressSortChangeEvents)
        {
            _currentSortColumn = value switch
            {
                "Name (A-Z)" => "DisplayName",
                "Name (Z-A)" => "DisplayName",
                "Protocol" => "Protocol",
                "Host / IP" => "Endpoint",
                _ => string.Empty
            };
            _isSortAscending = value != "Name (Z-A)";
            NotifyHeaderChanges();
        }
        ApplyFilterAndSort();
    }

    [RelayCommand]
    public void SortByColumn(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName)) return;

        if (string.Equals(CurrentSortColumn, columnName, StringComparison.OrdinalIgnoreCase))
        {
            IsSortAscending = !IsSortAscending;
        }
        else
        {
            CurrentSortColumn = columnName;
            IsSortAscending = true;
        }

        NotifyHeaderChanges();

        _suppressSortChangeEvents = true;
        switch (columnName)
        {
            case "DisplayName":
                SelectedSortOption = IsSortAscending ? "Name (A-Z)" : "Name (Z-A)";
                break;
            case "Protocol":
                SelectedSortOption = "Protocol";
                break;
            case "Endpoint":
                SelectedSortOption = "Host / IP";
                break;
            default:
                break;
        }
        _suppressSortChangeEvents = false;

        ApplyFilterAndSort();
    }

    public void NotifyHeaderChanges()
    {
        OnPropertyChanged(nameof(BookmarkSortIcon));
        OnPropertyChanged(nameof(BookmarkSortIconBrush));
        OnPropertyChanged(nameof(StatusSortIcon));
        OnPropertyChanged(nameof(StatusSortIconBrush));
        OnPropertyChanged(nameof(ProtocolSortIcon));
        OnPropertyChanged(nameof(ProtocolSortIconBrush));
        OnPropertyChanged(nameof(NameSortIcon));
        OnPropertyChanged(nameof(NameSortIconBrush));
        OnPropertyChanged(nameof(EndpointSortIcon));
        OnPropertyChanged(nameof(EndpointSortIconBrush));
        OnPropertyChanged(nameof(CredentialSortIcon));
        OnPropertyChanged(nameof(CredentialSortIconBrush));
        OnPropertyChanged(nameof(DisplayModeSortIcon));
        OnPropertyChanged(nameof(DisplayModeSortIconBrush));

        OnPropertyChanged(nameof(BookmarkHeader));
        OnPropertyChanged(nameof(StatusHeader));
        OnPropertyChanged(nameof(ProtocolHeader));
        OnPropertyChanged(nameof(NameHeader));
        OnPropertyChanged(nameof(EndpointHeader));
        OnPropertyChanged(nameof(CredentialHeader));
        OnPropertyChanged(nameof(DisplayModeHeader));
    }

    partial void OnIsGridViewChanged(bool value) => OnPropertyChanged(nameof(IsTableView));

    partial void OnIsFilterAllChanged(bool value)
    {
        if (_suppressFilterChangeEvents) return;
        if (value)
        {
            ResetFilters();
        }
    }

    partial void OnIsFilterBookmarkedChanged(bool value) => OnIndividualFilterToggled();
    partial void OnIsFilterRdpChanged(bool value) => OnIndividualFilterToggled();
    partial void OnIsFilterSshChanged(bool value) => OnIndividualFilterToggled();
    partial void OnIsFilterVncChanged(bool value) => OnIndividualFilterToggled();
    partial void OnIsFilterWebChanged(bool value) => OnIndividualFilterToggled();
    partial void OnIsFilterOnlineOnlyChanged(bool value) => OnIndividualFilterToggled();

    private void OnIndividualFilterToggled()
    {
        if (_suppressFilterChangeEvents) return;

        bool anyActive = IsFilterBookmarked || IsFilterRdp || IsFilterSsh || IsFilterVnc || IsFilterWeb || IsFilterOnlineOnly;
        _suppressFilterChangeEvents = true;
        if (!anyActive)
        {
            IsFilterAll = true;
            SelectedProtocolFilter = "All";
        }
        else
        {
            IsFilterAll = false;
            if (IsFilterBookmarked && !IsFilterRdp && !IsFilterSsh && !IsFilterVnc && !IsFilterWeb && !IsFilterOnlineOnly)
                SelectedProtocolFilter = "Bookmarked";
            else if (IsFilterRdp && !IsFilterBookmarked && !IsFilterSsh && !IsFilterVnc && !IsFilterWeb && !IsFilterOnlineOnly)
                SelectedProtocolFilter = "RDP";
            else if (IsFilterSsh && !IsFilterBookmarked && !IsFilterRdp && !IsFilterVnc && !IsFilterWeb && !IsFilterOnlineOnly)
                SelectedProtocolFilter = "SSH";
            else if (IsFilterVnc && !IsFilterBookmarked && !IsFilterRdp && !IsFilterSsh && !IsFilterWeb && !IsFilterOnlineOnly)
                SelectedProtocolFilter = "VNC";
            else if (IsFilterWeb && !IsFilterBookmarked && !IsFilterRdp && !IsFilterSsh && !IsFilterVnc && !IsFilterOnlineOnly)
                SelectedProtocolFilter = "Web";
            else
                SelectedProtocolFilter = "Multiple";
        }
        _suppressFilterChangeEvents = false;

        OnPropertyChanged(nameof(HasActiveFilter));
        OnPropertyChanged(nameof(ActiveFilterSummary));
        OnPropertyChanged(nameof(ActiveFilterBadgeText));
        ApplyFilterAndSort();
    }

    [RelayCommand]
    public void ResetFilters()
    {
        _suppressFilterChangeEvents = true;
        IsFilterAll = true;
        IsFilterBookmarked = false;
        IsFilterRdp = false;
        IsFilterSsh = false;
        IsFilterVnc = false;
        IsFilterWeb = false;
        IsFilterOnlineOnly = false;
        SelectedProtocolFilter = "All";
        _suppressFilterChangeEvents = false;

        OnPropertyChanged(nameof(HasActiveFilter));
        OnPropertyChanged(nameof(ActiveFilterSummary));
        OnPropertyChanged(nameof(ActiveFilterBadgeText));
        ApplyFilterAndSort();
    }

    [RelayCommand]
    public void SetProtocolFilter(string filter)
    {
        SelectedProtocolFilter = filter;
        if (string.Equals(filter, "All", StringComparison.OrdinalIgnoreCase))
        {
            ResetFilters();
        }
        else
        {
            _suppressFilterChangeEvents = true;
            IsFilterAll = false;
            IsFilterBookmarked = string.Equals(filter, "Bookmarked", StringComparison.OrdinalIgnoreCase);
            IsFilterRdp = string.Equals(filter, "RDP", StringComparison.OrdinalIgnoreCase);
            IsFilterSsh = string.Equals(filter, "SSH", StringComparison.OrdinalIgnoreCase);
            IsFilterVnc = string.Equals(filter, "VNC", StringComparison.OrdinalIgnoreCase);
            IsFilterWeb = string.Equals(filter, "Web", StringComparison.OrdinalIgnoreCase);
            IsFilterOnlineOnly = false;
            _suppressFilterChangeEvents = false;

            OnPropertyChanged(nameof(HasActiveFilter));
            OnPropertyChanged(nameof(ActiveFilterSummary));
            OnPropertyChanged(nameof(ActiveFilterBadgeText));
            ApplyFilterAndSort();
        }
    }

    [RelayCommand]
    public void SetViewMode(string mode)
    {
        IsGridView = string.Equals(mode, "Grid", StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    public void ToggleQuickConnect()
    {
        IsQuickConnectExpanded = !IsQuickConnectExpanded;
    }

    [RelayCommand]
    public void ClearSearch()
    {
        SearchText = string.Empty;
    }

    public void ApplyFilterAndSort()
    {
        IEnumerable<ConnectionCardViewModel> query = _allCards;

        // Multi-checkbox filtering
        if (!IsFilterAll)
        {
            var selectedProtocols = new List<ProtocolType>();
            if (IsFilterRdp) selectedProtocols.Add(ProtocolType.RDP);
            if (IsFilterSsh) selectedProtocols.Add(ProtocolType.SSH);
            if (IsFilterVnc) selectedProtocols.Add(ProtocolType.VNC);
            if (IsFilterWeb) selectedProtocols.Add(ProtocolType.Web);

            if (selectedProtocols.Count > 0)
            {
                query = query.Where(c => selectedProtocols.Contains(c.Protocol));
            }

            if (IsFilterBookmarked)
            {
                query = query.Where(c => c.IsBookmarked);
            }

            if (IsFilterOnlineOnly)
            {
                query = query.Where(c => c.PingStatus == ConnectionPingStatus.Online);
            }
        }
        else if (!string.IsNullOrWhiteSpace(SelectedProtocolFilter) && !string.Equals(SelectedProtocolFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(SelectedProtocolFilter, "Bookmarked", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(c => c.IsBookmarked);
            }
            else if (Enum.TryParse<ProtocolType>(SelectedProtocolFilter, true, out var proto))
            {
                query = query.Where(c => c.Protocol == proto);
            }
        }

        // Search Keyword
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim().ToLowerInvariant();
            query = query.Where(c =>
                c.DisplayName.ToLowerInvariant().Contains(term) ||
                c.Host.ToLowerInvariant().Contains(term) ||
                c.FullAddress.ToLowerInvariant().Contains(term) ||
                c.Protocol.ToString().ToLowerInvariant().Contains(term) ||
                c.CredentialTitle.ToLowerInvariant().Contains(term));
        }

        // Sorting: Bookmarked items always on top (unless explicitly sorting Bookmark descending)
        if (!string.IsNullOrEmpty(CurrentSortColumn))
        {
            query = (CurrentSortColumn, IsSortAscending) switch
            {
                ("Bookmark", true) => query.OrderByDescending(c => c.IsBookmarked).ThenBy(c => c.SortOrder).ThenBy(c => c.DisplayName),
                ("Bookmark", false) => query.OrderBy(c => c.IsBookmarked).ThenBy(c => c.SortOrder).ThenBy(c => c.DisplayName),

                ("Status", true) => query.OrderByDescending(c => c.IsBookmarked).ThenBy(c => c.PingStatus).ThenBy(c => c.DisplayName),
                ("Status", false) => query.OrderByDescending(c => c.IsBookmarked).ThenByDescending(c => c.PingStatus).ThenBy(c => c.DisplayName),

                ("Protocol", true) => query.OrderByDescending(c => c.IsBookmarked).ThenBy(c => c.Protocol).ThenBy(c => c.DisplayName),
                ("Protocol", false) => query.OrderByDescending(c => c.IsBookmarked).ThenByDescending(c => c.Protocol).ThenBy(c => c.DisplayName),

                ("DisplayName", true) => query.OrderByDescending(c => c.IsBookmarked).ThenBy(c => c.DisplayName),
                ("DisplayName", false) => query.OrderByDescending(c => c.IsBookmarked).ThenByDescending(c => c.DisplayName),

                ("Endpoint", true) => query.OrderByDescending(c => c.IsBookmarked).ThenBy(c => c.Host).ThenBy(c => c.Port),
                ("Endpoint", false) => query.OrderByDescending(c => c.IsBookmarked).ThenByDescending(c => c.Host).ThenByDescending(c => c.Port),

                ("Credential", true) => query.OrderByDescending(c => c.IsBookmarked).ThenBy(c => c.CredentialTitle).ThenBy(c => c.DisplayName),
                ("Credential", false) => query.OrderByDescending(c => c.IsBookmarked).ThenByDescending(c => c.CredentialTitle).ThenBy(c => c.DisplayName),

                ("DisplayMode", true) => query.OrderByDescending(c => c.IsBookmarked).ThenBy(c => c.DisplayModeText).ThenBy(c => c.DisplayName),
                ("DisplayMode", false) => query.OrderByDescending(c => c.IsBookmarked).ThenByDescending(c => c.DisplayModeText).ThenBy(c => c.DisplayName),

                _ => query.OrderByDescending(c => c.IsBookmarked).ThenBy(c => c.SortOrder).ThenBy(c => c.DisplayName)
            };
        }
        else
        {
            query = SelectedSortOption switch
            {
                "Custom Order" => query.OrderByDescending(c => c.IsBookmarked).ThenBy(c => c.SortOrder).ThenBy(c => c.DisplayName),
                "Name (A-Z)" => query.OrderByDescending(c => c.IsBookmarked).ThenBy(c => c.DisplayName),
                "Name (Z-A)" => query.OrderByDescending(c => c.IsBookmarked).ThenByDescending(c => c.DisplayName),
                "Protocol" => query.OrderByDescending(c => c.IsBookmarked).ThenBy(c => c.Protocol).ThenBy(c => c.DisplayName),
                "Host / IP" => query.OrderByDescending(c => c.IsBookmarked).ThenBy(c => c.Host).ThenBy(c => c.Port),
                "Port" => query.OrderByDescending(c => c.IsBookmarked).ThenBy(c => c.Port).ThenBy(c => c.DisplayName),
                _ => query.OrderByDescending(c => c.IsBookmarked).ThenBy(c => c.SortOrder).ThenBy(c => c.DisplayName)
            };
        }

        FilteredCards = new ObservableCollection<ConnectionCardViewModel>(query);
        OnPropertyChanged(nameof(ActiveFilterSummary));
    }

    [RelayCommand]
    public async Task ToggleBookmarkAsync(object? parameter)
    {
        ConnectionItem? model = parameter switch
        {
            ConnectionCardViewModel card => card.Model,
            ConnectionItem conn => conn,
            _ => null
        };

        if (model != null)
        {
            await _mainViewModel.ToggleBookmarkAsync(model);
        }
    }

    public async Task ReorderCardsAsync(ConnectionCardViewModel sourceCard, ConnectionCardViewModel targetCard)
    {
        if (sourceCard == null || targetCard == null) return;
        await _mainViewModel.ReorderConnectionItemAsync(sourceCard.Model, targetCard.Model);
    }

    [RelayCommand]
    public async Task PingCardAsync(ConnectionCardViewModel? card)
    {
        if (card != null)
        {
            await card.PingAsync();
            UpdateMetrics();
        }
    }

    [RelayCommand]
    public async Task PingAllAsync()
    {
        var targets = _allCards.ToList();
        if (IsPingingAll || targets.Count == 0) return;

        IsPingingAll = true;
        LogEngine.Instance.Info("Network", $"Starting batch ping test for {targets.Count} connections...");

        try
        {
            using var sem = new SemaphoreSlim(8);
            var tasks = targets.Select(async card =>
            {
                await sem.WaitAsync();
                try
                {
                    await card.PingAsync();
                    SafeDispatch(UpdateMetrics);
                }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(tasks);
            SafeDispatch(UpdateMetrics);
            LogEngine.Instance.Info("Network", "Batch ping test completed.");
        }
        finally
        {
            IsPingingAll = false;
        }
    }

    internal static Func<System.Windows.Threading.Dispatcher?> DispatcherProvider { get; set; } = () => System.Windows.Application.Current?.Dispatcher;

    internal Action<Action> SafeDispatch { get; set; } = action =>
    {
        var disp = DispatcherProvider();
        if (disp != null && disp.Thread.IsAlive && !disp.HasShutdownStarted && !disp.CheckAccess())
        {
            _ = disp.BeginInvoke(action);
        }
        else
        {
            action();
        }
    };

    [RelayCommand]
    public async Task ConnectCardAsync(ConnectionCardViewModel? card)
    {
        if (card != null)
        {
            await _mainViewModel.ConnectAsync(card.Model);
        }
    }

    [RelayCommand]
    public async Task ConnectTabbedAsync(ConnectionCardViewModel? card)
    {
        if (card != null)
        {
            await _mainViewModel.ConnectTabbedAsync(card.Model);
        }
    }

    [RelayCommand]
    public async Task ConnectFullscreenAsync(ConnectionCardViewModel? card)
    {
        if (card != null)
        {
            await _mainViewModel.ConnectFullscreenAsync(card.Model);
        }
    }

    [RelayCommand]
    public async Task ConnectExternalAsync(ConnectionCardViewModel? card)
    {
        if (card != null)
        {
            await _mainViewModel.ConnectExternalAsync(card.Model);
        }
    }

    [RelayCommand]
    public async Task AddConnectionAsync()
    {
        await _mainViewModel.AddConnectionAsync();
    }

    [RelayCommand]
    public async Task EditConnectionAsync(object? parameter)
    {
        ConnectionItem? model = parameter switch
        {
            ConnectionCardViewModel card => card.Model,
            ConnectionItem conn => conn,
            _ => null
        };

        if (model != null)
        {
            LogEngine.Instance.Info("UI", $"ConnectionsViewModel: Requesting edit for connection '{model.DisplayName}' (Id: {model.Id}).");
            await _mainViewModel.EditConnectionAsync(model);
        }
        else
        {
            LogEngine.Instance.Warn("UI", $"ConnectionsViewModel.EditConnectionAsync called with null or invalid parameter: '{parameter}'.");
        }
    }

    [RelayCommand]
    public async Task DuplicateConnectionAsync(object? parameter)
    {
        ConnectionItem? model = parameter switch
        {
            ConnectionCardViewModel card => card.Model,
            ConnectionItem conn => conn,
            _ => null
        };

        if (model != null)
        {
            var cloned = model.Clone();
            await _mainViewModel.SaveAndReloadConnectionAsync(cloned);
            LogEngine.Instance.Info("UI", $"Duplicated connection '{model.DisplayName}' to '{cloned.Name}'");
        }
    }

    [RelayCommand]
    public async Task DeleteConnectionAsync(object? parameter)
    {
        ConnectionItem? model = parameter switch
        {
            ConnectionCardViewModel card => card.Model,
            ConnectionItem conn => conn,
            _ => null
        };

        if (model != null)
        {
            await _mainViewModel.DeleteConnectionAsync(model);
        }
    }

    public static Action<string> SetClipboardText { get; set; } = text => Clipboard.SetText(text);

    [RelayCommand]
    public void CopyAddress(ConnectionCardViewModel? card)
    {
        if (card != null)
        {
            try
            {
                SetClipboardText(card.FullAddress);
                LogEngine.Instance.Debug("UI", $"Copied address '{card.FullAddress}' to clipboard.");
            }
            catch (Exception ex)
            {
                LogEngine.Instance.Error("UI", "Failed to copy address to clipboard", ex);
            }
        }
    }

    [RelayCommand]
    public async Task QuickConnectFromPageAsync()
    {
        if (string.IsNullOrWhiteSpace(QuickHost)) return;

        _mainViewModel.QuickConnectHost = QuickHost;
        _mainViewModel.QuickConnectProtocol = QuickProtocol;
        await _mainViewModel.QuickConnectAsync();
    }
}
