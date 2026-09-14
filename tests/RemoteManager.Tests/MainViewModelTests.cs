using System.Collections.ObjectModel;
using System.Diagnostics;
using RemoteManager.App.ViewModels;
using RemoteManager.Core.Models;
using RemoteManager.Data;
using RemoteManager.Data.Security;
using RemoteManager.Protocols.Rdp;
using Xunit;

namespace RemoteManager.Tests;

public class MainViewModelTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteDatabaseService _db;
    private readonly DpapiEncryptionService _crypto;
    private readonly MainViewModel _mainVm;

    public MainViewModelTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"RemoteManager_MainVM_{Guid.NewGuid():N}.db");
        _db = new SqliteDatabaseService(_tempDbPath);
        _crypto = new DpapiEncryptionService();
        _mainVm = new MainViewModel(_db, _crypto);
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
    public async Task MainViewModel_InitializeAsync_LoadsDataAndConfiguresSettings()
    {
        await _mainVm.InitializeAsync();

        Assert.NotNull(_mainVm.Settings);
        Assert.NotNull(_mainVm.Connections);
        Assert.NotNull(_mainVm.Credentials);
        Assert.NotNull(_mainVm.Groups);
        Assert.NotNull(_mainVm.ConnectionsVM);
    }

    [Fact]
    public async Task MainViewModel_LoadDataAsync_StripsProtocolPrefixAndSaves()
    {
        await _db.InitializeAsync();

        var prefixed = new ConnectionItem
        {
            Name = "RDP: Windows Server 2022",
            Host = "10.0.0.5",
            Port = 3389,
            Protocol = ProtocolType.RDP
        };
        await _db.SaveConnectionAsync(prefixed);

        await _mainVm.LoadDataAsync();

        var loaded = _mainVm.Connections.FirstOrDefault(c => c.Id == prefixed.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Windows Server 2022", loaded.Name);
    }

    [Fact]
    public void MainViewModel_SelectSession_TogglesIsSelectedProperly()
    {
        var tab1 = new SessionTabViewModel("Tab 1", "Tab1", null);
        var tab2 = new SessionTabViewModel("Tab 2", "Tab2", null);

        _mainVm.ActiveSessions.Add(tab1);
        _mainVm.ActiveSessions.Add(tab2);

        _mainVm.SelectedSession = tab1;
        Assert.True(tab1.IsSelected);
        Assert.False(tab2.IsSelected);

        _mainVm.SelectedSession = tab2;
        Assert.False(tab1.IsSelected);
        Assert.True(tab2.IsSelected);

        _mainVm.SelectedSession = null;
        Assert.False(tab2.IsSelected);
    }

    [Fact]
    public void MainViewModel_MoveConnectionToTop_ReordersCollections()
    {
        var c1 = new ConnectionItem { Name = "Alpha", Host = "10.0.0.1" };
        var c2 = new ConnectionItem { Name = "Beta", Host = "10.0.0.2" };

        _mainVm.Connections = new ObservableCollection<ConnectionItem> { c1, c2 };
        _mainVm.ConnectionsVM.SyncConnections(_mainVm.Connections, Array.Empty<Credential>());

        _mainVm.MoveConnectionToTop(c2);

        Assert.Equal("Beta", _mainVm.Connections[0].Name);
        Assert.Equal("Beta", _mainVm.ConnectionsVM.FilteredCards[0].DisplayName);
    }

    [Fact]
    public async Task MainViewModel_QuickConnectAsync_ParsesPortAndAvoidsDuplicates()
    {
        await _db.InitializeAsync();
        await _mainVm.LoadDataAsync();

        // 1. Empty target does nothing
        _mainVm.QuickConnectHost = "  ";
        await _mainVm.QuickConnectAsync();
        Assert.Empty(_mainVm.Connections);

        // 2. Custom port parsing host:port
        _mainVm.QuickConnectHost = "bastion.internal:2222";
        _mainVm.QuickConnectProtocol = ProtocolType.SSH;
        await _mainVm.QuickConnectAsync();

        var saved = _mainVm.Connections.FirstOrDefault(c => c.Host == "bastion.internal");
        Assert.NotNull(saved);
        Assert.Equal(2222, saved.Port);
        Assert.Equal(ProtocolType.SSH, saved.Protocol);

        // 3. Re-triggering same target reuses existing connection without creating duplicate
        var countBefore = _mainVm.Connections.Count;
        _mainVm.QuickConnectHost = "bastion.internal:2222";
        await _mainVm.QuickConnectAsync();
        Assert.Equal(countBefore, _mainVm.Connections.Count);
    }

    [Fact]
    public async Task MainViewModel_ConnectionCrud_AddEditSaveDelete()
    {
        await _db.InitializeAsync();
        await _mainVm.LoadDataAsync();

        // Add connection via callback
        ConnectionItem? requestedEditItem = null;
        _mainVm.RequestConnectionEditorPage = item => requestedEditItem = item;

        await _mainVm.AddConnectionAsync();
        Assert.Null(requestedEditItem); // Add passes null to editor

        // Save new connection
        var newConn = new ConnectionItem
        {
            Name = "Web Admin",
            Host = "admin.corp.local",
            Port = 443,
            Protocol = ProtocolType.Web
        };
        await _mainVm.SaveAndReloadConnectionAsync(newConn);

        Assert.Contains(_mainVm.Connections, c => c.Id == newConn.Id);

        // Edit connection via callback
        await _mainVm.EditConnectionAsync(newConn);
        Assert.Same(newConn, requestedEditItem);

        // Delete with rejected confirmation
        _mainVm.RequestConfirmWithNameAsync = (title, msg, expected, hint) => Task.FromResult(false);
        await _mainVm.DeleteConnectionAsync(newConn);
        Assert.Contains(_mainVm.Connections, c => c.Id == newConn.Id);

        // Delete with accepted confirmation
        _mainVm.RequestConfirmWithNameAsync = (title, msg, expected, hint) => Task.FromResult(true);
        await _mainVm.DeleteConnectionAsync(newConn);
        Assert.DoesNotContain(_mainVm.Connections, c => c.Id == newConn.Id);
    }

    [Fact]
    public async Task MainViewModel_CredentialCrud_AddEditSaveDelete()
    {
        await _db.InitializeAsync();
        await _mainVm.LoadDataAsync();

        Credential? requestedCred = null;
        _mainVm.RequestCredentialEditorPage = cred => requestedCred = cred;

        await _mainVm.AddCredentialAsync();
        Assert.Null(requestedCred);

        var newCred = new Credential
        {
            Title = "Database Admin",
            Username = "dbadmin",
            EncryptedPassword = _crypto.Encrypt("Secret123!")
        };
        await _mainVm.SaveAndReloadCredentialAsync(newCred);
        Assert.Contains(_mainVm.Credentials, c => c.Id == newCred.Id);

        await _mainVm.EditCredentialAsync(newCred);
        Assert.Same(newCred, requestedCred);

        // Delete rejected
        _mainVm.RequestConfirmationAsync = (title, msg) => Task.FromResult(false);
        await _mainVm.DeleteCredentialAsync(newCred);
        Assert.Contains(_mainVm.Credentials, c => c.Id == newCred.Id);

        // Delete approved
        _mainVm.RequestConfirmationAsync = (title, msg) => Task.FromResult(true);
        await _mainVm.DeleteCredentialAsync(newCred);
        Assert.DoesNotContain(_mainVm.Credentials, c => c.Id == newCred.Id);
    }

    [Fact]
    public async Task MainViewModel_Settings_SaveSettingsAsync_PersistsSuccessfully()
    {
        await _db.InitializeAsync();
        _mainVm.Settings = new AppSettings
        {
            Theme = "Dark",
            IsLoggingEnabled = true,
            MinimumLogLevel = "Warn"
        };

        await _mainVm.SaveSettingsAsync();

        var loaded = await _db.GetSettingsAsync();
        Assert.Equal("Dark", loaded.Theme);
        Assert.Equal("Warn", loaded.MinimumLogLevel);
    }

    [Fact]
    public async Task MainViewModel_GetCredentialTitle_ResolvesProperly()
    {
        await _db.InitializeAsync();
        var cred = new Credential { Title = "Root Key", Username = "root" };
        await _mainVm.SaveAndReloadCredentialAsync(cred);

        // Null id -> "None"
        Assert.Equal("None", _mainVm.GetCredentialTitle(null));

        // Matching id -> "Root Key"
        Assert.Equal("Root Key", _mainVm.GetCredentialTitle(cred.Id));

        // Non-existent id -> "Unknown"
        Assert.Equal("Unknown", _mainVm.GetCredentialTitle(Guid.NewGuid()));
    }

    [Fact]
    public async Task MainViewModel_ConnectModes_And_TabCallbacks()
    {
        await _db.InitializeAsync();
        var conn = new ConnectionItem
        {
            Name = "Server RDP",
            Host = "10.0.0.100",
            Port = 3389,
            Protocol = ProtocolType.RDP,
            DisplayMode = DisplayMode.Tabbed
        };
        await _mainVm.SaveAndReloadConnectionAsync(conn);

        SessionTabViewModel? popped = null;
        SessionTabViewModel? fullScreen = null;
        _mainVm.RequestPopOutWindow = s => popped = s;
        _mainVm.RequestFullscreenWindow = s => fullScreen = s;

        // 1. Create session tab and register lifecycle callbacks
        var tab = new SessionTabViewModel(conn, null);
        _mainVm.SetupTabCallbacks(tab);
        _mainVm.ActiveSessions.Add(tab);
        _mainVm.SelectedSession = tab;
        tab.Status = "Connecting";

        Assert.Single(_mainVm.ActiveSessions);
        Assert.Same(tab, _mainVm.SelectedSession);

        // 2. Re-connecting brings existing session to focus without duplicating
        await _mainVm.ConnectAsync(conn);
        Assert.Single(_mainVm.ActiveSessions);
        Assert.Same(tab, _mainVm.SelectedSession);

        // 3. Test pop out and fullscreen callbacks
        tab.PopOutCommand.Execute(null);
        Assert.Same(tab, popped);

        tab.FullscreenCommand.Execute(null);
        Assert.Same(tab, fullScreen);

        // 4. Test close callback with rejected confirmation
        _mainVm.RequestConfirmationAsync = (title, msg) => Task.FromResult(false);
        await tab.CloseCommand.ExecuteAsync(null);
        Assert.Single(_mainVm.ActiveSessions);

        // 5. Test close callback with approved confirmation
        _mainVm.RequestConfirmationAsync = (title, msg) => Task.FromResult(true);
        await tab.CloseCommand.ExecuteAsync(null);
        Assert.Empty(_mainVm.ActiveSessions);
    }


    [Fact]
    public async Task MainViewModel_DialogEditors_And_AlternativeConfirmations()
    {
        await _db.InitializeAsync();
        await _mainVm.LoadDataAsync();

        // Add Connection via RequestConnectionEditor dialog
        _mainVm.RequestConnectionEditorPage = null;
        _mainVm.RequestConnectionEditor = item => Task.FromResult<ConnectionItem?>(new ConnectionItem
        {
            Name = "Dialog Server",
            Host = "10.0.0.200",
            Port = 22,
            Protocol = ProtocolType.SSH
        });
        await _mainVm.AddConnectionAsync();
        var addedConn = _mainVm.Connections.FirstOrDefault(c => c.Name == "Dialog Server");
        Assert.NotNull(addedConn);

        // Edit Connection via dialog
        _mainVm.RequestConnectionEditor = item =>
        {
            item!.Name = "Dialog Server Renamed";
            return Task.FromResult<ConnectionItem?>(item);
        };
        await _mainVm.EditConnectionAsync(addedConn);
        Assert.Contains(_mainVm.Connections, c => c.Name == "Dialog Server Renamed");

        // Delete with RequestConfirmationAsync fallback
        _mainVm.RequestConfirmWithNameAsync = null;
        _mainVm.RequestConfirmationAsync = (title, msg) => Task.FromResult(true);
        await _mainVm.DeleteConnectionAsync(addedConn);
        Assert.DoesNotContain(_mainVm.Connections, c => c.Id == addedConn.Id);

        // Add Credential via RequestCredentialEditor dialog
        _mainVm.RequestCredentialEditorPage = null;
        _mainVm.RequestCredentialEditor = item => Task.FromResult<Credential?>(new Credential
        {
            Title = "Dialog Key",
            Username = "dialog_user"
        });
        await _mainVm.AddCredentialAsync();
        var addedCred = _mainVm.Credentials.FirstOrDefault(c => c.Title == "Dialog Key");
        Assert.NotNull(addedCred);

        // Edit Credential via dialog
        _mainVm.RequestCredentialEditor = item =>
        {
            item!.Title = "Dialog Key Renamed";
            return Task.FromResult<Credential?>(item);
        };
        await _mainVm.EditCredentialAsync(addedCred);
        Assert.Contains(_mainVm.Credentials, c => c.Title == "Dialog Key Renamed");

        // Delete Credential with RequestConfirmWithNameAsync
        _mainVm.RequestConfirmWithNameAsync = (title, msg, exp, hint) => Task.FromResult(true);
        await _mainVm.DeleteCredentialAsync(addedCred);
        Assert.DoesNotContain(_mainVm.Credentials, c => c.Id == addedCred.Id);
    }

    [Fact]
    public async Task MainViewModel_PageBasedEditors_And_ConfirmFallbacks()
    {
        await _db.InitializeAsync();
        var conn = new ConnectionItem { Name = "Server Page", Host = "10.0.0.9" };
        var cred = new Credential { Title = "Key Page", Username = "user9" };
        await _mainVm.SaveAndReloadConnectionAsync(conn);
        await _mainVm.SaveAndReloadCredentialAsync(cred);

        // 1. RequestConnectionEditorPage navigation callback
        ConnectionItem? requestedConn = null;
        _mainVm.RequestConnectionEditorPage = c => requestedConn = c;
        await _mainVm.AddConnectionAsync();
        Assert.Null(requestedConn); // Add passes null

        await _mainVm.EditConnectionAsync(conn);
        Assert.Same(conn, requestedConn);

        // 2. RequestCredentialEditorPage navigation callback
        Credential? requestedCred = null;
        _mainVm.RequestCredentialEditorPage = c => requestedCred = c;
        await _mainVm.AddCredentialAsync();
        Assert.Null(requestedCred); // Add passes null

        await _mainVm.EditCredentialAsync(cred);
        Assert.Same(cred, requestedCred);

        // 3. DeleteConnectionAsync with RequestConfirmWithNameAsync returning false
        _mainVm.RequestConfirmWithNameAsync = (t, m, e, h) => Task.FromResult(false);
        await _mainVm.DeleteConnectionAsync(conn);
        Assert.Contains(_mainVm.Connections, c => c.Id == conn.Id);

        // 4. DeleteConnectionAsync with RequestConfirmation fallback
        _mainVm.RequestConfirmWithNameAsync = null;
        _mainVm.RequestConfirmationAsync = null;
        _mainVm.RequestConfirmation = (t, m) => false;
        await _mainVm.DeleteConnectionAsync(conn);
        Assert.Contains(_mainVm.Connections, c => c.Id == conn.Id);

        _mainVm.RequestConfirmation = (t, m) => true;
        await _mainVm.DeleteConnectionAsync(conn);
        Assert.DoesNotContain(_mainVm.Connections, c => c.Id == conn.Id);

        // 5. DeleteCredentialAsync with RequestConfirmation fallback
        _mainVm.RequestConfirmWithNameAsync = null;
        _mainVm.RequestConfirmationAsync = null;
        _mainVm.RequestConfirmation = (t, m) => false;
        await _mainVm.DeleteCredentialAsync(cred);
        Assert.Contains(_mainVm.Credentials, c => c.Id == cred.Id);

        _mainVm.RequestConfirmation = (t, m) => true;
        await _mainVm.DeleteCredentialAsync(cred);
        Assert.DoesNotContain(_mainVm.Credentials, c => c.Id == cred.Id);
    }

    [Fact]
    public async Task MainViewModel_ConnectFullscreenAndTabbed_FocusesExistingActiveSession()
    {
        await _db.InitializeAsync();
        var conn = new ConnectionItem { Name = "Active Target", Host = "10.0.0.10", Port = 22, Protocol = ProtocolType.SSH };
        await _mainVm.SaveAndReloadConnectionAsync(conn);

        var activeTab = new SessionTabViewModel(conn, null) { Status = "Connecting" };
        _mainVm.ActiveSessions.Add(activeTab);

        await _mainVm.ConnectFullscreenAsync(conn);
        Assert.Same(activeTab, _mainVm.SelectedSession);

        await _mainVm.ConnectTabbedAsync(conn);
        Assert.Same(activeTab, _mainVm.SelectedSession);
    }

    [Fact]
    public async Task MainViewModel_ConnectExternalAsync_ExecutesConnection()
    {
        await _db.InitializeAsync();
        var badSsh = new ConnectionItem { Name = "Bad SSH Target", Host = "invalid;host", Port = 22, Protocol = ProtocolType.SSH };
        await _mainVm.SaveAndReloadConnectionAsync(badSsh);

        // ConnectExternalAsync moves connection to top and delegates to SshSessionHandler
        await Assert.ThrowsAsync<ArgumentException>(() => _mainVm.ConnectExternalAsync(badSsh));
        Assert.Equal("Bad SSH Target", _mainVm.Connections[0].Name);
    }

    [Fact]
    public async Task MainViewModel_ConnectWithCredentialResolution_And_QuickConnectProtocols()
    {
        await _db.InitializeAsync();
        var cred = new Credential
        {
            Title = "SSH Cred",
            Username = "sshuser",
            EncryptedPassword = _crypto.Encrypt("MyPass123!")
        };
        await _mainVm.SaveAndReloadCredentialAsync(cred);

        var conn = new ConnectionItem
        {
            Name = "Authed SSH",
            Host = "invalid;host",
            Port = 22,
            Protocol = ProtocolType.SSH,
            CredentialId = cred.Id
        };
        await _mainVm.SaveAndReloadConnectionAsync(conn);

        // 1. Decrypts credential and attempts connection
        await Assert.ThrowsAsync<ArgumentException>(() => _mainVm.ConnectExternalAsync(conn));

        // 2. QuickConnect default ports
        var rdpConn = new ConnectionItem { Name = "RDP Target", Host = "rdp.target", Port = 3389, Protocol = ProtocolType.RDP };
        await _mainVm.SaveAndReloadConnectionAsync(rdpConn);
        var rdpTab = new SessionTabViewModel(rdpConn, null) { Status = "Connecting" };
        _mainVm.ActiveSessions.Add(rdpTab);

        _mainVm.QuickConnectHost = "rdp.target";
        _mainVm.QuickConnectProtocol = ProtocolType.RDP;
        await _mainVm.QuickConnectAsync();
        Assert.Same(rdpTab, _mainVm.SelectedSession);

        var vncConn = new ConnectionItem { Name = "VNC Target", Host = "vnc.target", Port = 5900, Protocol = ProtocolType.VNC };
        await _mainVm.SaveAndReloadConnectionAsync(vncConn);
        var vncTab = new SessionTabViewModel(vncConn, null) { Status = "Connecting" };
        _mainVm.ActiveSessions.Add(vncTab);

        _mainVm.QuickConnectHost = "vnc.target";
        _mainVm.QuickConnectProtocol = ProtocolType.VNC;
        await _mainVm.QuickConnectAsync();
        Assert.Same(vncTab, _mainVm.SelectedSession);

        var webConn = new ConnectionItem { Name = "Web Target", Host = "web.target", Port = 443, Protocol = ProtocolType.Web };
        await _mainVm.SaveAndReloadConnectionAsync(webConn);
        var webTab = new SessionTabViewModel(webConn, null) { Status = "Connecting" };
        _mainVm.ActiveSessions.Add(webTab);

        _mainVm.QuickConnectHost = "web.target";
        _mainVm.QuickConnectProtocol = ProtocolType.Web;
        await _mainVm.QuickConnectAsync();
        Assert.Same(webTab, _mainVm.SelectedSession);

        // 3. SelectSessionCommand
        _mainVm.SelectSessionCommand.Execute(null); // null safety
        _mainVm.SelectSessionCommand.Execute(rdpTab);
        Assert.Same(rdpTab, _mainVm.SelectedSession);
    }

    [Fact]
    public async Task MainViewModel_Connect_WithSessionControlFactory_TabbedAndFullscreen()
    {
        await _db.InitializeAsync();
        _mainVm.SessionControlFactory = c => new object();

        var rdpConn = new ConnectionItem { Name = "RDP Factory", Host = "10.0.0.1", Protocol = ProtocolType.RDP };
        var webConn = new ConnectionItem { Name = "Web Factory", Host = "10.0.0.2", Protocol = ProtocolType.Web };
        var vncConn = new ConnectionItem { Name = "VNC Factory", Host = "10.0.0.3", Protocol = ProtocolType.VNC };

        await _mainVm.SaveAndReloadConnectionAsync(rdpConn);
        await _mainVm.SaveAndReloadConnectionAsync(webConn);
        await _mainVm.SaveAndReloadConnectionAsync(vncConn);

        // RDP tabbed
        await _mainVm.ConnectTabbedAsync(rdpConn);
        Assert.Contains(_mainVm.ActiveSessions, s => s.Connection.Id == rdpConn.Id);

        // Web tabbed
        await _mainVm.ConnectTabbedAsync(webConn);
        Assert.Contains(_mainVm.ActiveSessions, s => s.Connection.Id == webConn.Id);

        // VNC tabbed
        await _mainVm.ConnectTabbedAsync(vncConn);
        Assert.Contains(_mainVm.ActiveSessions, s => s.Connection.Id == vncConn.Id);
    }

    [Fact]
    public async Task MainViewModel_Connect_ErrorPaths_ShowErrorMessage_And_ShowWarningMessage()
    {
        await _db.InitializeAsync();

        // 1. Decrypt failure calls ShowErrorMessage
        string? errTitle = null, errMsg = null;
        _mainVm.ShowErrorMessage = (t, m) => { errTitle = t; errMsg = m; };
        var badCred = new Credential { Title = "Bad Cipher", EncryptedPassword = "INVALID_NOT_BASE64_CIPHER!!!" };
        await _mainVm.SaveAndReloadCredentialAsync(badCred);
        var badCredConn = new ConnectionItem { Name = "Bad Cred Conn", Host = "invalid;host", Protocol = ProtocolType.SSH, CredentialId = badCred.Id };
        await _mainVm.SaveAndReloadConnectionAsync(badCredConn);

        await Assert.ThrowsAsync<ArgumentException>(() => _mainVm.ConnectExternalAsync(badCredConn));
        Assert.Equal("Security Error", errTitle);
        Assert.NotNull(errMsg);

        // 2. VNC external launch failure calls ShowWarningMessage
        string? warnTitle = null, warnMsg = null;
        _mainVm.ShowWarningMessage = (t, m) => { warnTitle = t; warnMsg = m; };
        var extVncConn = new ConnectionItem { Name = "Ext VNC", Host = "10.0.0.1", Port = 5900, Protocol = ProtocolType.VNC, DisplayMode = DisplayMode.ExternalApp };
        await _mainVm.SaveAndReloadConnectionAsync(extVncConn);
        _mainVm.Settings.VncClientType = "Custom";
        _mainVm.Settings.CustomVncClientPath = @"C:\NonExistentXYZ\Vnc.exe";

        await _mainVm.ConnectExternalAsync(extVncConn);
        Assert.Equal("VNC Error", warnTitle);
        Assert.NotNull(warnMsg);
    }

    [Fact]
    public async Task MainViewModel_Connect_RdpExternal_WithProcessLauncher()
    {
        await _db.InitializeAsync();
        var prev = RdpIsolatedLauncher.ProcessLauncher;
        try
        {
            ProcessStartInfo? psi = null;
            RdpIsolatedLauncher.ProcessLauncher = p => { psi = p; return new Process(); };
            var extRdpConn = new ConnectionItem { Name = "Ext RDP", Host = "10.0.0.1", Port = 3389, Protocol = ProtocolType.RDP, DisplayMode = DisplayMode.ExternalApp };
            await _mainVm.SaveAndReloadConnectionAsync(extRdpConn);

            await _mainVm.ConnectExternalAsync(extRdpConn);
            Assert.NotNull(psi);
            Assert.Equal("mstsc.exe", psi.FileName);
        }
        finally
        {
            RdpIsolatedLauncher.ProcessLauncher = prev;
        }
    }

    [Fact]
    public async Task MainViewModel_Connect_VncExternal_Success()
    {
        await _db.InitializeAsync();
        var prev = RemoteManager.Protocols.Vnc.VncSessionHandler.ProcessLauncher;
        var tempExe = Path.Combine(Path.GetTempPath(), $"VncSuccess_{Guid.NewGuid():N}.exe");
        File.WriteAllText(tempExe, "dummy");
        try
        {
            RemoteManager.Protocols.Vnc.VncSessionHandler.ProcessLauncher = psi => new Process();
            var extVncConn = new ConnectionItem { Name = "Ext VNC Ok", Host = "10.0.0.1", Port = 5900, Protocol = ProtocolType.VNC, DisplayMode = DisplayMode.ExternalApp };
            await _mainVm.SaveAndReloadConnectionAsync(extVncConn);
            _mainVm.Settings.VncClientType = "Custom";
            _mainVm.Settings.CustomVncClientPath = tempExe;

            await _mainVm.ConnectExternalAsync(extVncConn);
        }
        finally
        {
            RemoteManager.Protocols.Vnc.VncSessionHandler.ProcessLauncher = prev;
            if (File.Exists(tempExe)) File.Delete(tempExe);
        }
    }

    private class FakeRdpControl : RemoteManager.Protocols.Rdp.IRdpHostControl
    {
        public event Action? DisconnectRequested;
        public event Action<string, int, int>? Disconnected;
        public event Action? Connected;

        public bool DisconnectCalled { get; set; }
        public void Connect(string server, int port, string? username, string? domain, string? password, int width = 1920, int height = 1080) { }
        public void Disconnect() => DisconnectCalled = true;
        public void RaiseDisconnectRequested() => DisconnectRequested?.Invoke();
        public void RaiseConnected() => Connected?.Invoke();
        public void RaiseDisconnected(string description, int discReason, int extReason) => Disconnected?.Invoke(description, discReason, extReason);
    }

    private class FakeWebControl : RemoteManager.Protocols.Web.IWebViewSessionControl
    {
        public Task NavigateAsync(string url) => Task.CompletedTask;
    }

    private class FakeVncControl : RemoteManager.Protocols.Vnc.IVncHostControl
    {
        public event Action? Connected;
        public event Action? Disconnected;
        public event Action<string>? Error;
        public bool DisconnectCalled { get; set; }
        public bool DisposeCalled { get; set; }

        public void Connect(string host, int port, string? password) { }
        public void Disconnect() => DisconnectCalled = true;
        public void Dispose() => DisposeCalled = true;

        public void RaiseConnected() => Connected?.Invoke();
        public void RaiseDisconnected() => Disconnected?.Invoke();
        public void RaiseError(string msg) => Error?.Invoke(msg);
    }

    [Fact]
    public async Task MainViewModel_SessionControls_FullLifecycle_Rdp_Web_Vnc()
    {
        await _db.InitializeAsync();

        var fakeRdp = new FakeRdpControl();
        var fakeWeb = new FakeWebControl();
        var fakeVnc = new FakeVncControl();

        _mainVm.SessionControlFactory = item => item.Protocol switch
        {
            ProtocolType.RDP => fakeRdp,
            ProtocolType.Web => fakeWeb,
            ProtocolType.VNC => fakeVnc,
            _ => new object()
        };

        var rdpConn = new ConnectionItem { Name = "RDP Mock", Host = "10.0.0.1", Protocol = ProtocolType.RDP };
        var webConn = new ConnectionItem { Name = "Web Mock", Host = "https://10.0.0.2", Protocol = ProtocolType.Web };
        var vncConn = new ConnectionItem { Name = "VNC Mock", Host = "10.0.0.3", Protocol = ProtocolType.VNC };

        // 1. RDP connection, events, and DisconnectRequested
        await _mainVm.ConnectTabbedAsync(rdpConn);
        var rdpSession = _mainVm.ActiveSessions.FirstOrDefault(s => s.Connection.Id == rdpConn.Id);
        Assert.NotNull(rdpSession);

        fakeRdp.RaiseConnected();
        Assert.Equal("Connected", rdpSession.Status);
        Assert.True(rdpSession.IsConnected);

        // When another user connects (extended reason 5)
        fakeRdp.RaiseDisconnected("Another user connected to the remote computer", 2, 5);
        Assert.Equal("Disconnected (Another user connected)", rdpSession.Status);
        Assert.False(rdpSession.IsConnected);

        // General disconnect
        fakeRdp.RaiseDisconnected("Server closed connection", 3, 0);
        Assert.Equal("Disconnected", rdpSession.Status);
        Assert.False(rdpSession.IsConnected);

        fakeRdp.RaiseDisconnectRequested();
        // Closing tab should call Disconnect()
        _mainVm.RequestConfirmationAsync = (t, m) => Task.FromResult(true);
        await rdpSession.CloseCommand.ExecuteAsync(null);
        Assert.True(fakeRdp.DisconnectCalled);

        // 2. Web connection
        await _mainVm.ConnectTabbedAsync(webConn);
        var webSession = _mainVm.ActiveSessions.FirstOrDefault(s => s.Connection.Id == webConn.Id);
        Assert.NotNull(webSession);

        // 3. VNC connection and events
        await _mainVm.ConnectTabbedAsync(vncConn);
        var vncSession = _mainVm.ActiveSessions.FirstOrDefault(s => s.Connection.Id == vncConn.Id);
        Assert.NotNull(vncSession);

        fakeVnc.RaiseConnected();
        Assert.Equal("Connected", vncSession.Status);
        Assert.True(vncSession.IsConnected);

        fakeVnc.RaiseDisconnected();
        Assert.Equal("Disconnected", vncSession.Status);
        Assert.False(vncSession.IsConnected);

        fakeVnc.RaiseError("Handshake failed");
        Assert.Equal("Error: Handshake failed", vncSession.Status);

        // Close VNC session tab -> calls vnc.Disconnect() and vnc.Dispose()
        await vncSession.CloseCommand.ExecuteAsync(null);
        Assert.True(fakeVnc.DisconnectCalled);
        Assert.True(fakeVnc.DisposeCalled);
    }

    [Fact]
    public async Task MainViewModel_QuickConnectAsync_DefaultProtocolFallback()
    {
        await _db.InitializeAsync();
        _mainVm.SessionControlFactory = c => new object();

        _mainVm.QuickConnectHost = "10.0.0.99";
        _mainVm.QuickConnectProtocol = (ProtocolType)999; // unknown enum value

        await _mainVm.QuickConnectAsync();
        var conn = _mainVm.Connections.FirstOrDefault(s => s.Host == "10.0.0.99");
        Assert.NotNull(conn);
        Assert.Equal(3389, conn.Port);
    }

    [Fact]
    public async Task MainViewModel_EditConnectionAsync_SupportsMultipleParameterTypes()
    {
        await _db.InitializeAsync();
        var conn = new ConnectionItem { Name = "MainVM Edit Test", Host = "10.0.0.50" };
        await _mainVm.SaveAndReloadConnectionAsync(conn);

        var card = new ConnectionCardViewModel(conn);

        ConnectionItem? pageOpened = null;
        _mainVm.RequestConnectionEditorPage = item => pageOpened = item;

        // 1. Passing ConnectionItem
        await _mainVm.EditConnectionAsync(conn);
        Assert.NotNull(pageOpened);
        Assert.Equal("MainVM Edit Test", pageOpened.DisplayName);

        // 2. Passing ConnectionCardViewModel
        pageOpened = null;
        await _mainVm.EditConnectionAsync(card);
        Assert.NotNull(pageOpened);
        Assert.Equal("MainVM Edit Test", pageOpened.DisplayName);

        // 3. Passing null or invalid param (null safe)
        pageOpened = null;
        await _mainVm.EditConnectionAsync(null);
        Assert.Null(pageOpened);

        await _mainVm.EditConnectionAsync(12345);
        Assert.Null(pageOpened);
    }

    [Fact]
    public void RdpAxClient_FormatDisconnectReason_ReturnsMeaningfulDescriptions()
    {
        // When another user connects (extended reason 5)
        var msgOtherUser = RemoteManager.Protocols.Rdp.RdpAxClient.FormatDisconnectReason(2, 5);
        Assert.Contains("another user connected", msgOtherUser, StringComparison.OrdinalIgnoreCase);

        // When user was logged off (extended reason 2)
        var msgLogoff = RemoteManager.Protocols.Rdp.RdpAxClient.FormatDisconnectReason(2, 2);
        Assert.Contains("logged off", msgLogoff, StringComparison.OrdinalIgnoreCase);

        // Server terminated (discReason 3)
        var msgServer = RemoteManager.Protocols.Rdp.RdpAxClient.FormatDisconnectReason(3, 0);
        Assert.Contains("terminated by the remote server", msgServer, StringComparison.OrdinalIgnoreCase);

        // DNS lookup failed (discReason 260)
        var msgDns = RemoteManager.Protocols.Rdp.RdpAxClient.FormatDisconnectReason(260, 0);
        Assert.Contains("resolve", msgDns, StringComparison.OrdinalIgnoreCase);

        // Timeout (discReason 264)
        var msgTimeout = RemoteManager.Protocols.Rdp.RdpAxClient.FormatDisconnectReason(264, 0);
        Assert.Contains("timed out", msgTimeout, StringComparison.OrdinalIgnoreCase);

        // Socket closed (discReason 2308)
        var msgSocket = RemoteManager.Protocols.Rdp.RdpAxClient.FormatDisconnectReason(2308, 0);
        Assert.Contains("lost", msgSocket, StringComparison.OrdinalIgnoreCase);

        // Low on memory (extended reason 6)
        var msgMemory = RemoteManager.Protocols.Rdp.RdpAxClient.FormatDisconnectReason(1, 6);
        Assert.Contains("memory", msgMemory, StringComparison.OrdinalIgnoreCase);

        // License issue (extended reason 260)
        var msgLicense = RemoteManager.Protocols.Rdp.RdpAxClient.FormatDisconnectReason(1, 260);
        Assert.Contains("license", msgLicense, StringComparison.OrdinalIgnoreCase);

        // Fallback
        var msgGeneric = RemoteManager.Protocols.Rdp.RdpAxClient.FormatDisconnectReason(999, 888);
        Assert.Contains("999", msgGeneric);
        Assert.Contains("888", msgGeneric);
    }

    [Fact]
    public async Task MainViewModel_RdpDisconnect_AnotherUserConnected_UpdatesStatusAndState()
    {
        await _db.InitializeAsync();
        var fakeRdp = new FakeRdpControl();
        _mainVm.SessionControlFactory = _ => fakeRdp;

        var rdpConn = new ConnectionItem { Name = "Win11 Workstation", Host = "192.168.1.100", Protocol = ProtocolType.RDP };
        await _mainVm.ConnectTabbedAsync(rdpConn);

        var session = _mainVm.ActiveSessions.FirstOrDefault(s => s.Connection.Id == rdpConn.Id);
        Assert.NotNull(session);

        fakeRdp.RaiseConnected();
        Assert.Equal("Connected", session.Status);
        Assert.True(session.IsConnected);

        // Another user logs in to the machine
        fakeRdp.RaiseDisconnected("You have been disconnected because another connection was made to the remote computer.", 2, 5);

        Assert.Equal("Disconnected (Another user connected)", session.Status);
        Assert.False(session.IsConnected);
    }

    [Fact]
    public async Task MainViewModel_CloseTab_WhenDisconnected_DoesNotAskConfirmation()
    {
        await _db.InitializeAsync();
        var fakeRdp = new FakeRdpControl();
        _mainVm.SessionControlFactory = _ => fakeRdp;

        var rdpConn = new ConnectionItem { Name = "Win11 Disconnected Close", Host = "192.168.1.101", Protocol = ProtocolType.RDP };
        await _mainVm.ConnectTabbedAsync(rdpConn);

        var session = _mainVm.ActiveSessions.FirstOrDefault(s => s.Connection.Id == rdpConn.Id);
        Assert.NotNull(session);

        // Disconnect the session
        fakeRdp.RaiseDisconnected("Disconnected by server", 3, 5);
        Assert.False(session.IsConnected);

        bool confirmationPrompted = false;
        _mainVm.RequestConfirmationAsync = (t, m) =>
        {
            confirmationPrompted = true;
            return Task.FromResult(false); // Even if it would return false, it shouldn't be asked
        };

        // Close the tab
        await session.CloseCommand.ExecuteAsync(null);

        // Confirmation modal should NOT have been prompted
        Assert.False(confirmationPrompted);
        // And session should be removed
        Assert.DoesNotContain(session, _mainVm.ActiveSessions);
    }

    [Fact]
    public async Task MainViewModel_CloseTab_WhenConnected_AsksConfirmation()
    {
        await _db.InitializeAsync();
        var fakeRdp = new FakeRdpControl();
        _mainVm.SessionControlFactory = _ => fakeRdp;

        var rdpConn = new ConnectionItem { Name = "Win11 Connected Close", Host = "192.168.1.102", Protocol = ProtocolType.RDP };
        await _mainVm.ConnectTabbedAsync(rdpConn);

        var session = _mainVm.ActiveSessions.FirstOrDefault(s => s.Connection.Id == rdpConn.Id);
        Assert.NotNull(session);

        fakeRdp.RaiseConnected();
        Assert.True(session.IsConnected);

        bool confirmationPrompted = false;
        _mainVm.RequestConfirmationAsync = (t, m) =>
        {
            confirmationPrompted = true;
            return Task.FromResult(false); // User cancels confirmation
        };

        // Attempt to close tab
        await session.CloseCommand.ExecuteAsync(null);

        // Confirmation was prompted, and user cancelled, so session remains active
        Assert.True(confirmationPrompted);
        Assert.Contains(session, _mainVm.ActiveSessions);
    }
}





