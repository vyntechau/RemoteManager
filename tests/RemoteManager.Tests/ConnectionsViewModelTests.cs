using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using RemoteManager.App.ViewModels;
using RemoteManager.Core.Models;
using RemoteManager.Data;
using RemoteManager.Data.Security;
using RemoteManager.Protocols.Rdp;
using Xunit;
using ProtocolType = RemoteManager.Core.Models.ProtocolType;

namespace RemoteManager.Tests;

public class ConnectionsViewModelTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteDatabaseService _db;
    private readonly DpapiEncryptionService _crypto;
    private readonly MainViewModel _mainVm;
    private readonly ConnectionsViewModel _vm;

    public ConnectionsViewModelTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"RemoteManager_ConnVM_{Guid.NewGuid():N}.db");
        _db = new SqliteDatabaseService(_tempDbPath);
        _crypto = new DpapiEncryptionService();
        _mainVm = new MainViewModel(_db, _crypto);
        _vm = _mainVm.ConnectionsVM;
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_tempDbPath))
            {
                File.Delete(_tempDbPath);
            }
        }
        catch { }
    }

    [Fact]
    public void ConnectionCardViewModel_DisplayModeText_CoversAllModes()
    {
        var cardTabbed = new ConnectionCardViewModel(new ConnectionItem { DisplayMode = DisplayMode.Tabbed });
        var cardFs = new ConnectionCardViewModel(new ConnectionItem { DisplayMode = DisplayMode.Fullscreen });
        var cardExt = new ConnectionCardViewModel(new ConnectionItem { DisplayMode = DisplayMode.ExternalApp });
        var cardDetached = new ConnectionCardViewModel(new ConnectionItem { DisplayMode = DisplayMode.DetachedWindow });

        Assert.Equal("Embedded Tab", cardTabbed.DisplayModeText);
        Assert.Equal("Fullscreen (F11)", cardFs.DisplayModeText);
        Assert.Equal("Native Window", cardExt.DisplayModeText);
        Assert.Equal("DetachedWindow", cardDetached.DisplayModeText);
    }

    [Fact]
    public void ConnectionCardViewModel_BadgeColorsAndLightBg_CoversAllProtocols()
    {
        var rdp = new ConnectionCardViewModel(new ConnectionItem { Protocol = ProtocolType.RDP });
        var ssh = new ConnectionCardViewModel(new ConnectionItem { Protocol = ProtocolType.SSH });
        var vnc = new ConnectionCardViewModel(new ConnectionItem { Protocol = ProtocolType.VNC });
        var web = new ConnectionCardViewModel(new ConnectionItem { Protocol = ProtocolType.Web });

        Assert.Equal("#0078D4", rdp.ProtocolBadgeColor);
        Assert.Equal("#200078D4", rdp.ProtocolBadgeLightBg);

        Assert.Equal("#107C41", ssh.ProtocolBadgeColor);
        Assert.Equal("#20107C41", ssh.ProtocolBadgeLightBg);

        Assert.Equal("#D83B01", vnc.ProtocolBadgeColor);
        Assert.Equal("#20D83B01", vnc.ProtocolBadgeLightBg);

        Assert.Equal("#881798", web.ProtocolBadgeColor);
        Assert.Equal("#20881798", web.ProtocolBadgeLightBg);
    }

    [Fact]
    public void ConnectionCardViewModel_PingStatusPresentation_CoversAllStates()
    {
        var card = new ConnectionCardViewModel(new ConnectionItem { Host = "127.0.0.1", Port = 12345 });

        // Untested
        Assert.Equal(ConnectionPingStatus.Untested, card.PingStatus);
        Assert.Equal("Untested", card.PingStatusText);
        Assert.Equal("#8A8886", card.PingStatusColor);
        Assert.False(card.IsOnline);
        Assert.False(card.IsOffline);

        // Testing
        card.PingStatus = ConnectionPingStatus.Testing;
        Assert.Equal("Testing...", card.PingStatusText);
        Assert.Equal("#FFB900", card.PingStatusColor);

        // Online without latency
        card.PingStatus = ConnectionPingStatus.Online;
        card.LatencyMs = null;
        Assert.Equal("Online", card.PingStatusText);
        Assert.Equal("#107C41", card.PingStatusColor);
        Assert.True(card.IsOnline);
        Assert.False(card.IsOffline);

        // Online with latency
        card.LatencyMs = 24;
        Assert.Equal("24 ms", card.PingStatusText);

        // Offline
        card.PingStatus = ConnectionPingStatus.Offline;
        Assert.Equal("Unreachable", card.PingStatusText);
        Assert.Equal("#E81123", card.PingStatusColor);
        Assert.False(card.IsOnline);
        Assert.True(card.IsOffline);
    }

    [Fact]
    public async Task ConnectionCardViewModel_PingAsync_UnreachableHost_BecomesOffline()
    {
        // Port 1 on 127.0.0.1 is virtually guaranteed to be closed or fail immediately
        var card = new ConnectionCardViewModel(new ConnectionItem { Host = "127.0.0.1", Port = 1 });
        await card.PingAsync();

        Assert.Equal(ConnectionPingStatus.Offline, card.PingStatus);
        Assert.False(card.IsPinging);
    }

    [Fact]
    public void ConnectionsViewModel_ViewModeAndQuickConnect_ToggleProperly()
    {
        Assert.True(_vm.IsGridView);
        Assert.False(_vm.IsTableView);

        _vm.SetViewMode("Table");
        Assert.False(_vm.IsGridView);
        Assert.True(_vm.IsTableView);

        _vm.SetViewMode("Grid");
        Assert.True(_vm.IsGridView);
        Assert.False(_vm.IsTableView);

        Assert.False(_vm.IsQuickConnectExpanded);
        _vm.ToggleQuickConnect();
        Assert.True(_vm.IsQuickConnectExpanded);
        _vm.ToggleQuickConnect();
        Assert.False(_vm.IsQuickConnectExpanded);
    }

    [Fact]
    public void ConnectionsViewModel_MoveCardToTop_ReordersCards()
    {
        var c1 = new ConnectionItem { Name = "First", Host = "10.0.0.1" };
        var c2 = new ConnectionItem { Name = "Second", Host = "10.0.0.2" };
        var c3 = new ConnectionItem { Name = "Third", Host = "10.0.0.3" };

        _vm.SyncConnections(new[] { c1, c2, c3 }, Array.Empty<Credential>());
        Assert.Equal("First", _vm.FilteredCards[0].DisplayName);

        _vm.MoveCardToTop(c3.Id);
        Assert.Equal("Third", _vm.FilteredCards[0].DisplayName);
        Assert.Equal("First", _vm.FilteredCards[1].DisplayName);
    }

    [Fact]
    public void ConnectionsViewModel_ProtocolFilterProperties_UpdateAccurately()
    {
        _vm.SetProtocolFilter("All");
        Assert.True(_vm.IsFilterAll);
        Assert.False(_vm.IsFilterRdp);

        _vm.SetProtocolFilter("RDP");
        Assert.True(_vm.IsFilterRdp);
        Assert.False(_vm.IsFilterAll);

        _vm.SetProtocolFilter("SSH");
        Assert.True(_vm.IsFilterSsh);

        _vm.SetProtocolFilter("VNC");
        Assert.True(_vm.IsFilterVnc);

        _vm.SetProtocolFilter("Web");
        Assert.True(_vm.IsFilterWeb);
    }

    [Fact]
    public void ConnectionsViewModel_AllSortOptions_OrderItemsCorrectly()
    {
        var items = new List<ConnectionItem>
        {
            new() { Name = "B-Server", Host = "192.168.1.50", Port = 5900, Protocol = ProtocolType.VNC },
            new() { Name = "A-Server", Host = "10.0.0.10", Port = 22, Protocol = ProtocolType.SSH },
            new() { Name = "C-Server", Host = "172.16.0.5", Port = 3389, Protocol = ProtocolType.RDP }
        };

        _vm.SyncConnections(items, Array.Empty<Credential>());

        // Name (A-Z)
        _vm.SelectedSortOption = "Name (A-Z)";
        Assert.Equal("A-Server", _vm.FilteredCards[0].DisplayName);
        Assert.Equal("B-Server", _vm.FilteredCards[1].DisplayName);
        Assert.Equal("C-Server", _vm.FilteredCards[2].DisplayName);

        // Name (Z-A)
        _vm.SelectedSortOption = "Name (Z-A)";
        Assert.Equal("C-Server", _vm.FilteredCards[0].DisplayName);
        Assert.Equal("B-Server", _vm.FilteredCards[1].DisplayName);
        Assert.Equal("A-Server", _vm.FilteredCards[2].DisplayName);

        // Protocol
        _vm.SelectedSortOption = "Protocol";
        Assert.Equal(ProtocolType.RDP, _vm.FilteredCards[0].Protocol);
        Assert.Equal(ProtocolType.SSH, _vm.FilteredCards[1].Protocol);
        Assert.Equal(ProtocolType.VNC, _vm.FilteredCards[2].Protocol);

        // Host / IP
        _vm.SelectedSortOption = "Host / IP";
        Assert.Equal("10.0.0.10", _vm.FilteredCards[0].Host);
        Assert.Equal("172.16.0.5", _vm.FilteredCards[1].Host);
        Assert.Equal("192.168.1.50", _vm.FilteredCards[2].Host);

        // Port
        _vm.SelectedSortOption = "Port";
        Assert.Equal(22, _vm.FilteredCards[0].Port);
        Assert.Equal(3389, _vm.FilteredCards[1].Port);
        Assert.Equal(5900, _vm.FilteredCards[2].Port);
    }

    [Fact]
    public async Task ConnectionsViewModel_PingCardAsync_ExecutesGracefully()
    {
        var item = new ConnectionItem { Name = "Ping Test", Host = "127.0.0.1", Port = 1 };
        _vm.SyncConnections(new[] { item }, Array.Empty<Credential>());

        // Null card should not throw
        await _vm.PingCardAsync(null);

        // Valid card
        var card = _vm.FilteredCards[0];
        await _vm.PingCardAsync(card);
        Assert.Equal(ConnectionPingStatus.Offline, card.PingStatus);
    }

    [Fact]
    public async Task ConnectionsViewModel_PingAllAsync_ExecutesBatchPings()
    {
        var item1 = new ConnectionItem { Name = "Host 1", Host = "127.0.0.1", Port = 1 };
        var item2 = new ConnectionItem { Name = "Host 2", Host = "127.0.0.1", Port = 2 };
        _vm.SyncConnections(new[] { item1, item2 }, Array.Empty<Credential>());

        await _vm.PingAllAsync();
        Assert.False(_vm.IsPingingAll);
        Assert.Equal(ConnectionPingStatus.Offline, _vm.FilteredCards[0].PingStatus);
        Assert.Equal(ConnectionPingStatus.Offline, _vm.FilteredCards[1].PingStatus);
    }

    [Fact]
    public async Task ConnectionsViewModel_ConnectCommands_DelegateToMainViewModel()
    {
        await _db.InitializeAsync();
        var item = new ConnectionItem { Name = "Server Alpha", Host = "10.0.0.1", Port = 22, Protocol = ProtocolType.SSH };
        await _mainVm.SaveAndReloadConnectionAsync(item);
        _vm.SyncConnections(_mainVm.Connections, Array.Empty<Credential>());
        var card = _vm.FilteredCards[0];

        // Existing session brings to focus without spawning external apps
        var session = new SessionTabViewModel(item, null) { Status = "Connecting" };
        _mainVm.ActiveSessions.Add(session);

        // Null card safety
        await _vm.ConnectCardAsync(null);
        await _vm.ConnectTabbedAsync(null);
        await _vm.ConnectFullscreenAsync(null);
        await _vm.ConnectExternalAsync(null);

        // Active card delegation
        await _vm.ConnectCardAsync(card);
        Assert.Same(session, _mainVm.SelectedSession);

        await _vm.ConnectTabbedAsync(card);
        Assert.Same(session, _mainVm.SelectedSession);

        await _vm.ConnectFullscreenAsync(card);
        Assert.Same(session, _mainVm.SelectedSession);
    }

    [Fact]
    public async Task ConnectionsViewModel_CrudCommands_AddEditDuplicateDelete()
    {
        await _db.InitializeAsync();
        var item = new ConnectionItem { Name = "Original Server", Host = "192.168.1.1", Port = 3389 };
        await _mainVm.SaveAndReloadConnectionAsync(item);
        _vm.SyncConnections(_mainVm.Connections, Array.Empty<Credential>());
        var card = _vm.FilteredCards[0];

        // 1. Add connection delegation
        ConnectionItem? addedItem = null;
        _mainVm.RequestConnectionEditor = existing =>
        {
            addedItem = new ConnectionItem { Name = "Added Server", Host = "192.168.1.2", Port = 22 };
            return Task.FromResult<ConnectionItem?>(addedItem);
        };
        await _vm.AddConnectionAsync();
        Assert.Contains(_mainVm.Connections, c => c.Name == "Added Server");

        // 2. Edit connection delegation
        _vm.SyncConnections(_mainVm.Connections, Array.Empty<Credential>());
        card = _vm.FilteredCards.First(c => c.DisplayName == "Original Server");
        _mainVm.RequestConnectionEditor = existing =>
        {
            existing!.Name = "Renamed Server";
            return Task.FromResult<ConnectionItem?>(existing);
        };
        await _vm.EditConnectionAsync(null); // null safety
        await _vm.EditConnectionAsync(card);
        Assert.Contains(_mainVm.Connections, c => c.Name == "Renamed Server");

        // 3. Duplicate connection delegation
        _vm.SyncConnections(_mainVm.Connections, Array.Empty<Credential>());
        card = _vm.FilteredCards.First(c => c.DisplayName == "Renamed Server");
        await _vm.DuplicateConnectionAsync(null); // null safety
        await _vm.DuplicateConnectionAsync(card);
        Assert.Contains(_mainVm.Connections, c => c.Name == "Renamed Server (Copy)");

        // 4. Delete connection delegation
        _vm.SyncConnections(_mainVm.Connections, Array.Empty<Credential>());
        var copyCard = _vm.FilteredCards.First(c => c.DisplayName == "Renamed Server (Copy)");
        _mainVm.RequestConfirmationAsync = (title, msg) => Task.FromResult(true);
        await _vm.DeleteConnectionAsync(null); // null safety
        await _vm.DeleteConnectionAsync(copyCard);
        Assert.DoesNotContain(_mainVm.Connections, c => c.Name == "Renamed Server (Copy)");
    }

    [Fact]
    public async Task ConnectionsViewModel_QuickConnectFromPageAsync_DelegatesCorrectly()
    {
        await _db.InitializeAsync();

        // Empty host does nothing
        _vm.QuickHost = "";
        await _vm.QuickConnectFromPageAsync();
        Assert.Empty(_mainVm.Connections);

        // Valid host triggers quick connect
        _vm.QuickHost = "quick.internal:2222";
        _vm.QuickProtocol = ProtocolType.SSH;
        await _vm.QuickConnectFromPageAsync();

        Assert.Contains(_mainVm.Connections, c => c.Host == "quick.internal" && c.Port == 2222);
    }

    [Fact]
    public async Task ConnectionCardViewModel_PingAsync_OnlineStatusWithTcpListener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var card = new ConnectionCardViewModel(new ConnectionItem { Host = "127.0.0.1", Port = port });

            await card.PingAsync();
            Assert.Equal(ConnectionPingStatus.Online, card.PingStatus);
            Assert.NotNull(card.LatencyMs);
            Assert.True(card.LatencyMs >= 1);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void ConnectionsViewModel_SortOptions_Property_IsPopulated()
    {
        Assert.NotEmpty(_vm.SortOptions);
        Assert.Contains("Name (A-Z)", _vm.SortOptions);
    }

    [Fact]
    public async Task ConnectionsViewModel_ConnectExternalAsync_WithCard_Executes()
    {
        await _db.InitializeAsync();
        var item = new ConnectionItem { Name = "Bad SSH", Host = "invalid;host", Port = 22, Protocol = ProtocolType.SSH };
        await _mainVm.SaveAndReloadConnectionAsync(item);
        _vm.SyncConnections(_mainVm.Connections, Array.Empty<Credential>());
        var card = _vm.FilteredCards.First(c => c.DisplayName == "Bad SSH");

        await Assert.ThrowsAsync<ArgumentException>(() => _vm.ConnectExternalAsync(card));
    }

    [Fact]
    public void ConnectionsViewModel_CopyAddress_WithCard_CopiesToClipboard()
    {
        string? copied = null;
        var prev = ConnectionsViewModel.SetClipboardText;
        try
        {
            ConnectionsViewModel.SetClipboardText = text => copied = text;
            var item = new ConnectionItem { Name = "Clipboard Host", Host = "10.0.0.99", Port = 3389 };
            var card = new ConnectionCardViewModel(item);

            _vm.CopyAddress(null); // null safety
            Assert.Null(copied);

            _vm.CopyAddress(card);
            Assert.Equal("10.0.0.99:3389", copied);
        }
        finally
        {
            ConnectionsViewModel.SetClipboardText = prev;
        }
    }

    [Fact]
    public void ConnectionsViewModel_CopyAddress_NonStaThread_CatchesException()
    {
        var prev = ConnectionsViewModel.SetClipboardText;
        try
        {
            ConnectionsViewModel.SetClipboardText = _ => throw new ThreadStateException("MTA not supported");
            var item = new ConnectionItem { Name = "Clipboard Host", Host = "10.0.0.99", Port = 3389 };
            var card = new ConnectionCardViewModel(item);

            // On thread exception, CopyAddress catches exception cleanly without throwing
            _vm.CopyAddress(card);
        }
        finally
        {
            ConnectionsViewModel.SetClipboardText = prev;
        }
    }

    [Fact]
    public async Task ConnectionsViewModel_ConnectExternalAsync_SuccessCard()
    {
        var prev = RdpIsolatedLauncher.ProcessLauncher;
        try
        {
            RdpIsolatedLauncher.ProcessLauncher = psi => new Process();
            var item = new ConnectionItem { Name = "Valid Ext RDP", Host = "10.0.0.1", Port = 3389, Protocol = ProtocolType.RDP };
            var card = new ConnectionCardViewModel(item);

            await _vm.ConnectExternalAsync(card);
        }
        finally
        {
            RdpIsolatedLauncher.ProcessLauncher = prev;
        }
    }

    [Fact]
    public void ConnectionsViewModel_SafeDispatch_ExecutesDirectly()
    {
        var prev = ConnectionsViewModel.DispatcherProvider;
        try
        {
            // 1. With null dispatcher -> executes action directly
            ConnectionsViewModel.DispatcherProvider = () => null;
            bool called = false;
            _vm.SafeDispatch(() => called = true);
            Assert.True(called);

            // 2. With real dispatcher if present -> executes BeginInvoke
            if (System.Windows.Application.Current?.Dispatcher != null)
            {
                ConnectionsViewModel.DispatcherProvider = () => System.Windows.Application.Current.Dispatcher;
                _vm.SafeDispatch(() => { });
            }
        }
        finally
        {
            ConnectionsViewModel.DispatcherProvider = prev;
        }
    }

    [Fact]
    public async Task ConnectionsViewModel_EditConnectionAsync_SupportsMultipleParameterTypes()
    {
        await _db.InitializeAsync();
        var conn = new ConnectionItem { Name = "Param Test Server", Host = "127.0.0.1", Protocol = ProtocolType.SSH };
        await _mainVm.SaveAndReloadConnectionAsync(conn);
        _vm.SyncConnections(_mainVm.Connections, Array.Empty<Credential>());

        var card = _vm.FilteredCards.First(c => c.DisplayName == "Param Test Server");

        ConnectionItem? edited = null;
        _mainVm.RequestConnectionEditorPage = item => edited = item;

        // 1. Passing ConnectionCardViewModel
        await _vm.EditConnectionAsync(card);
        Assert.NotNull(edited);
        Assert.Equal("Param Test Server", edited.DisplayName);

        // 2. Passing raw ConnectionItem directly
        edited = null;
        await _vm.EditConnectionAsync(conn);
        Assert.NotNull(edited);
        Assert.Equal("Param Test Server", edited.DisplayName);

        // 3. Passing null or unsupported type (null safety)
        edited = null;
        await _vm.EditConnectionAsync(null);
        Assert.Null(edited);

        await _vm.EditConnectionAsync("invalid_param");
        Assert.Null(edited);
    }
}


