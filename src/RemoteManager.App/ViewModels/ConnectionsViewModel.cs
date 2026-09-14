using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteManager.Core.Logging;
using RemoteManager.Core.Models;

namespace RemoteManager.App.ViewModels;

public partial class ConnectionsViewModel : ObservableObject
{
    private readonly MainViewModel _mainViewModel;
    private readonly List<ConnectionCardViewModel> _allCards = [];

    [ObservableProperty]
    private ObservableCollection<ConnectionCardViewModel> _filteredCards = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilterAll))]
    [NotifyPropertyChangedFor(nameof(IsFilterRdp))]
    [NotifyPropertyChangedFor(nameof(IsFilterSsh))]
    [NotifyPropertyChangedFor(nameof(IsFilterVnc))]
    [NotifyPropertyChangedFor(nameof(IsFilterWeb))]
    private string _selectedProtocolFilter = "All";

    public bool IsFilterAll => string.Equals(SelectedProtocolFilter, "All", StringComparison.OrdinalIgnoreCase);
    public bool IsFilterRdp => string.Equals(SelectedProtocolFilter, "RDP", StringComparison.OrdinalIgnoreCase);
    public bool IsFilterSsh => string.Equals(SelectedProtocolFilter, "SSH", StringComparison.OrdinalIgnoreCase);
    public bool IsFilterVnc => string.Equals(SelectedProtocolFilter, "VNC", StringComparison.OrdinalIgnoreCase);
    public bool IsFilterWeb => string.Equals(SelectedProtocolFilter, "Web", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    private string _selectedSortOption = "Name (A-Z)";

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
        RdpCount = _allCards.Count(c => c.Protocol == ProtocolType.RDP);
        SshCount = _allCards.Count(c => c.Protocol == ProtocolType.SSH);
        VncCount = _allCards.Count(c => c.Protocol == ProtocolType.VNC);
        WebCount = _allCards.Count(c => c.Protocol == ProtocolType.Web);
        OnlineCount = _allCards.Count(c => c.PingStatus == ConnectionPingStatus.Online);
    }

    public void MoveCardToTop(Guid connectionId)
    {
        var card = _allCards.FirstOrDefault(c => c.Model.Id == connectionId);
        if (card != null)
        {
            var idx = _allCards.IndexOf(card);
            if (idx > 0)
            {
                _allCards.RemoveAt(idx);
                _allCards.Insert(0, card);
            }

            var fIdx = FilteredCards.IndexOf(card);
            if (fIdx > 0)
            {
                FilteredCards.Move(fIdx, 0);
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
    partial void OnSelectedProtocolFilterChanged(string value) => ApplyFilterAndSort();
    partial void OnSelectedSortOptionChanged(string value) => ApplyFilterAndSort();
    partial void OnIsGridViewChanged(bool value) => OnPropertyChanged(nameof(IsTableView));

    [RelayCommand]
    public void SetProtocolFilter(string filter)
    {
        SelectedProtocolFilter = filter;
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

        // Protocol Filter
        if (!string.IsNullOrWhiteSpace(SelectedProtocolFilter) && !string.Equals(SelectedProtocolFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            if (Enum.TryParse<ProtocolType>(SelectedProtocolFilter, true, out var proto))
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

        // Sorting
        query = SelectedSortOption switch
        {
            "Name (Z-A)" => query.OrderByDescending(c => c.DisplayName),
            "Protocol" => query.OrderBy(c => c.Protocol).ThenBy(c => c.DisplayName),
            "Host / IP" => query.OrderBy(c => c.Host).ThenBy(c => c.Port),
            "Port" => query.OrderBy(c => c.Port).ThenBy(c => c.DisplayName),
            _ => query.OrderBy(c => c.DisplayName)
        };

        FilteredCards = new ObservableCollection<ConnectionCardViewModel>(query);
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
