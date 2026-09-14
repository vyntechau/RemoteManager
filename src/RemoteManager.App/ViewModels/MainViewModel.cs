using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteManager.Core.Interfaces;
using RemoteManager.Core.Logging;
using RemoteManager.Core.Models;
using RemoteManager.Protocols.Rdp;
using RemoteManager.Protocols.Ssh;
using RemoteManager.Protocols.Vnc;
using RemoteManager.Protocols.Web;

namespace RemoteManager.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IDatabaseService _databaseService;
    private readonly IEncryptionService _encryptionService;

    [ObservableProperty]
    private ObservableCollection<ConnectionItem> _connections = [];

    [ObservableProperty]
    private ObservableCollection<ConnectionItem> _filteredConnections = [];

    [ObservableProperty]
    private ObservableCollection<Credential> _credentials = [];

    [ObservableProperty]
    private ObservableCollection<ConnectionGroup> _groups = [];

    [ObservableProperty]
    private ObservableCollection<SessionTabViewModel> _activeSessions = [];

    [ObservableProperty]
    private SessionTabViewModel? _selectedSession;

    partial void OnSelectedSessionChanged(SessionTabViewModel? oldValue, SessionTabViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue != null) newValue.IsSelected = true;
    }

    [RelayCommand]
    private void SelectSession(SessionTabViewModel? session)
    {
        if (session != null)
        {
            SelectedSession = session;
        }
    }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private AppSettings _settings = new();

    [ObservableProperty]
    private int _selectedNavIndex = 0; // 0: Connections, 1: Credentials, 2: Settings

    // Quick Connect Fields
    [ObservableProperty]
    private string _quickConnectHost = string.Empty;

    [ObservableProperty]
    private ProtocolType _quickConnectProtocol = ProtocolType.RDP;

    // Callbacks for Page Navigation & UI confirmations
    public Action<ConnectionItem?>? RequestConnectionEditorPage { get; set; }
    public Action<Credential?>? RequestCredentialEditorPage { get; set; }
    public Func<string, string, Task<bool>>? RequestConfirmationAsync { get; set; }
    public Func<string, string, string, string, Task<bool>>? RequestConfirmWithNameAsync { get; set; }
    public Func<ConnectionItem?, Task<ConnectionItem?>>? RequestConnectionEditor { get; set; }
    public Func<Credential?, Task<Credential?>>? RequestCredentialEditor { get; set; }
    public Func<string, string, bool>? RequestConfirmation { get; set; }
    public Action<SessionTabViewModel>? RequestPopOutWindow { get; set; }
    public Action<SessionTabViewModel>? RequestFullscreenWindow { get; set; }
    public Action<string, string> ShowErrorMessage { get; set; } = (title, msg) => MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Error);
    public Action<string, string> ShowWarningMessage { get; set; } = (title, msg) => MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    internal Func<ConnectionItem, object?>? SessionControlFactory { get; set; }

    public ConnectionsViewModel ConnectionsVM { get; }

    public MainViewModel(IDatabaseService databaseService, IEncryptionService encryptionService)
    {
        _databaseService = databaseService;
        _encryptionService = encryptionService;
        ConnectionsVM = new ConnectionsViewModel(this);
    }

    public async Task InitializeAsync()
    {
        await _databaseService.InitializeAsync();
        Settings = await _databaseService.GetSettingsAsync();
        LogEngine.Instance.Configure(Settings);

        await LoadDataAsync();

        // On startup ping all items
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            await ConnectionsVM.PingAllAsync();
        });
    }

    public async Task LoadDataAsync()
    {
        var conns = await _databaseService.GetAllConnectionsAsync();
        var creds = await _databaseService.GetAllCredentialsAsync();
        var groups = await _databaseService.GetAllGroupsAsync();

        foreach (var conn in conns)
        {
            var prefix = $"{conn.Protocol}:";
            if (!string.IsNullOrEmpty(conn.Name) && conn.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                conn.Name = conn.Name.Substring(prefix.Length).TrimStart();
                _ = _databaseService.SaveConnectionAsync(conn);
            }
        }

        Connections = new ObservableCollection<ConnectionItem>(conns);
        Credentials = new ObservableCollection<Credential>(creds);
        Groups = new ObservableCollection<ConnectionGroup>(groups);

        ApplyFilter();
        ConnectionsVM.SyncConnections(Connections, Credentials);
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
        if (ConnectionsVM != null && ConnectionsVM.SearchText != value)
        {
            ConnectionsVM.SearchText = value;
        }
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            FilteredConnections = new ObservableCollection<ConnectionItem>(Connections);
            return;
        }

        var lower = SearchText.Trim().ToLowerInvariant();
        var filtered = Connections.Where(c =>
            c.DisplayName.ToLowerInvariant().Contains(lower) ||
            c.Name.ToLowerInvariant().Contains(lower) ||
            c.Host.ToLowerInvariant().Contains(lower) ||
            c.FullAddress.ToLowerInvariant().Contains(lower) ||
            c.Protocol.ToString().ToLowerInvariant().Contains(lower));

        FilteredConnections = new ObservableCollection<ConnectionItem>(filtered);
    }

    [RelayCommand]
    public async Task ConnectAsync(ConnectionItem connection)
    {
        await ConnectWithModeAsync(connection, connection.DisplayMode);
    }

    [RelayCommand]
    public async Task ConnectTabbedAsync(ConnectionItem connection)
    {
        await ConnectWithModeAsync(connection, DisplayMode.Tabbed);
    }

    [RelayCommand]
    public async Task ConnectFullscreenAsync(ConnectionItem connection)
    {
        await ConnectWithModeAsync(connection, DisplayMode.Fullscreen);
    }

    [RelayCommand]
    public async Task ConnectExternalAsync(ConnectionItem connection)
    {
        await ConnectWithModeAsync(connection, DisplayMode.ExternalApp);
    }

    public void MoveConnectionToTop(ConnectionItem connection)
    {
        var target = Connections.FirstOrDefault(c => c.Id == connection.Id) ?? connection;
        var idx = Connections.IndexOf(target);
        if (idx > 0)
        {
            Connections.Move(idx, 0);
        }
        ApplyFilter();

        ConnectionsVM?.MoveCardToTop(target.Id);
    }

    private async Task ConnectWithModeAsync(ConnectionItem connection, DisplayMode mode)
    {
        MoveConnectionToTop(connection);
        LogEngine.Instance.Info("Session", $"Requesting connection to '{connection.Name}' ({connection.Protocol}://{connection.Host}:{connection.Port}) in {mode} mode.");

        // If this connection is already open and active/connecting in a tab, focus it instead of duplicating
        if (mode != DisplayMode.ExternalApp)
        {
            var existingSession = ActiveSessions.FirstOrDefault(s => s.Connection.Id == connection.Id && (s.IsConnected || s.Status == "Connecting" || s.Status == "Loading"));
            if (existingSession != null)
            {
                LogEngine.Instance.Info("Session", $"Found existing active session tab for '{connection.Name}'. Bringing to focus.");
                SelectedSession = existingSession;
                return;
            }
        }

        // Retrieve and decrypt credential if assigned
        Credential? cred = null;
        string? decryptedPassword = null;

        if (connection.CredentialId.HasValue)
        {
            cred = await _databaseService.GetCredentialByIdAsync(connection.CredentialId.Value);
            if (cred != null && !string.IsNullOrEmpty(cred.EncryptedPassword))
            {
                try
                {
                    decryptedPassword = _encryptionService.Decrypt(cred.EncryptedPassword);
                    LogEngine.Instance.Debug("Security", $"Credentials '{cred.Title}' successfully decrypted for user '{cred.Username}'.");
                }
                catch (Exception ex)
                {
                    LogEngine.Instance.Error("Security", $"Failed to decrypt credentials '{cred.Title}'", ex);
                    ShowErrorMessage("Security Error", $"Failed to decrypt credentials: {ex.Message}");
                }
            }
        }

        switch (connection.Protocol)
        {
            case ProtocolType.RDP:
                if (mode == DisplayMode.ExternalApp)
                {
                    LogEngine.Instance.Info("Session", $"Launching RDP external app for '{connection.Name}'.");
                    RdpIsolatedLauncher.Launch(connection.Host, connection.Port, cred?.Username, cred?.Domain, decryptedPassword, fullScreen: false);
                    return;
                }

                // Tabbed or Fullscreen mode: Embedded ActiveX inside WPF with full action controls
                var rdpHost = SessionControlFactory?.Invoke(connection) ?? new RdpHostControl();
                var rdpSession = new SessionTabViewModel(connection, rdpHost);
                SetupTabCallbacks(rdpSession);

                ActiveSessions.Add(rdpSession);
                SelectedSession = rdpSession;
                rdpSession.Status = "Connecting";

                // Connect after tab is in visual tree
                _ = Task.Run(async () =>
                {
                    await Task.Delay(300); // Give control time to initialize handle
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        try
                        {
                            if (rdpHost is IRdpHostControl rdpControl)
                            {
                                rdpControl.Connect(connection.Host, connection.Port, cred?.Username, cred?.Domain, decryptedPassword);
                            }
                            rdpSession.Status = "Connected";
                            rdpSession.IsConnected = true;

                            if (mode == DisplayMode.Fullscreen)
                            {
                                RequestFullscreenWindow?.Invoke(rdpSession);
                            }
                        }
                        catch (Exception ex)
                        {
                            rdpSession.Status = "Error: " + ex.Message;
                        }
                    });
                });
                break;

            case ProtocolType.Web:
                var webHost = SessionControlFactory?.Invoke(connection) ?? new WebView2SessionControl(connection.Id);
                var webSession = new SessionTabViewModel(connection, webHost);
                SetupTabCallbacks(webSession);

                ActiveSessions.Add(webSession);
                SelectedSession = webSession;
                webSession.Status = "Loading";

                if (webHost is IWebViewSessionControl webCtrl)
                {
                    _ = webCtrl.NavigateAsync(connection.Host).ContinueWith(t =>
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            webSession.Status = t.IsFaulted ? "Failed" : "Connected";
                            webSession.IsConnected = !t.IsFaulted;
                        });
                    });
                }
                break;

            case ProtocolType.SSH:
                SshSessionHandler.Launch(connection.Host, connection.Port, cred?.Username, Settings);
                break;

            case ProtocolType.VNC:
                // If explicitly set to ExternalApp or configured for external viewer in settings
                if (connection.DisplayMode == DisplayMode.ExternalApp || (Settings.VncClientType != "BuiltIn" && connection.DisplayMode != DisplayMode.Tabbed && connection.DisplayMode != DisplayMode.Fullscreen))
                {
                    try
                    {
                        VncSessionHandler.Launch(connection.Host, connection.Port, decryptedPassword, Settings);
                    }
                    catch (Exception ex)
                    {
                        ShowWarningMessage("VNC Error", $"Failed to launch external VNC viewer: {ex.Message}");
                    }
                    break;
                }

                // Built-in VNC Viewer (Embedded Tab)
                var vncHost = SessionControlFactory?.Invoke(connection) ?? new VncHostControl();
                var vncSession = new SessionTabViewModel(connection, vncHost);
                SetupTabCallbacks(vncSession);

                ActiveSessions.Add(vncSession);
                SelectedSession = vncSession;
                vncSession.Status = "Connecting";

                if (vncHost is IVncHostControl vncControl)
                {
                    vncControl.Connected += () =>
                    {
                        var disp = Application.Current?.Dispatcher;
                        if (disp != null && !disp.CheckAccess())
                        {
                            disp.Invoke(() =>
                            {
                                vncSession.Status = "Connected";
                                vncSession.IsConnected = true;
                            });
                        }
                        else
                        {
                            vncSession.Status = "Connected";
                            vncSession.IsConnected = true;
                        }
                    };
                    vncControl.Disconnected += () =>
                    {
                        var disp = Application.Current?.Dispatcher;
                        if (disp != null && !disp.CheckAccess())
                        {
                            disp.Invoke(() =>
                            {
                                vncSession.Status = "Disconnected";
                                vncSession.IsConnected = false;
                            });
                        }
                        else
                        {
                            vncSession.Status = "Disconnected";
                            vncSession.IsConnected = false;
                        }
                    };
                    vncControl.Error += (err) =>
                    {
                        var disp = Application.Current?.Dispatcher;
                        if (disp != null && !disp.CheckAccess())
                        {
                            disp.Invoke(() =>
                            {
                                vncSession.Status = $"Error: {err}";
                            });
                        }
                        else
                        {
                            vncSession.Status = $"Error: {err}";
                        }
                    };
                }

                _ = Task.Run(async () =>
                {
                    await Task.Delay(300); // Give control time to attach
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        try
                        {
                            if (vncHost is IVncHostControl vncCtrl)
                            {
                                vncCtrl.Connect(connection.Host, connection.Port, decryptedPassword);
                            }
                            if (mode == DisplayMode.Fullscreen)
                            {
                                RequestFullscreenWindow?.Invoke(vncSession);
                            }
                        }
                        catch (Exception ex)
                        {
                            vncSession.Status = "Error: " + ex.Message;
                        }
                    });
                });
                break;
        }
    }

    internal void SetupTabCallbacks(SessionTabViewModel session)
    {
        if (session.Content is IRdpHostControl rdpControl)
        {
            rdpControl.Connected += () =>
            {
                var disp = Application.Current?.Dispatcher;
                if (disp != null && !disp.CheckAccess())
                {
                    disp.Invoke(() =>
                    {
                        session.Status = "Connected";
                        session.IsConnected = true;
                    });
                }
                else
                {
                    session.Status = "Connected";
                    session.IsConnected = true;
                }
            };

            rdpControl.Disconnected += (desc, discReason, extReason) =>
            {
                var disp = Application.Current?.Dispatcher;
                string status = extReason == 5
                    ? "Disconnected (Another user connected)"
                    : "Disconnected";

                if (disp != null && !disp.CheckAccess())
                {
                    disp.Invoke(() =>
                    {
                        session.Status = status;
                        session.IsConnected = false;
                    });
                }
                else
                {
                    session.Status = status;
                    session.IsConnected = false;
                }

                LogEngine.Instance.Warn("Session", $"RDP session '{session.Title}' disconnected (Reason: {discReason}, Extended: {extReason}): {desc}");
            };

            rdpControl.DisconnectRequested += () =>
            {
                var disp = Application.Current?.Dispatcher;
                if (disp != null && !disp.CheckAccess())
                {
                    disp.Invoke(() =>
                    {
                        _ = session.CloseCommand.ExecuteAsync(null);
                    });
                }
                else
                {
                    _ = session.CloseCommand.ExecuteAsync(null);
                }
            };
        }

        session.OnCloseRequested = async (s) =>
        {
            bool isDisconnected = s.Status.StartsWith("Disconnected", StringComparison.OrdinalIgnoreCase);

            // Do not show confirmation modal when disconnected or for utility tabs
            if (!isDisconnected && !s.IsUtilityTab && RequestConfirmationAsync != null)
            {
                var confirmed = await RequestConfirmationAsync.Invoke(
                    "Close Connection",
                    $"Disconnect and close the session to '{s.Title}'?");
                if (!confirmed) return;
            }

            LogEngine.Instance.Info("Session", $"Closing session tab '{s.Title}' ({s.Connection.Protocol}://{s.Connection.Host}:{s.Connection.Port})");
            if (s.Content is IRdpHostControl rdp)
            {
                rdp.Disconnect();
            }
            else if (s.Content is IVncHostControl vnc)
            {
                vnc.Disconnect();
                vnc.Dispose();
            }
            ActiveSessions.Remove(s);
            if (SelectedSession == s)
            {
                SelectedSession = ActiveSessions.LastOrDefault();
            }
        };

        session.OnPopOutRequested = (s) =>
        {
            LogEngine.Instance.Info("Session", $"Popping out session tab '{s.Title}' into detached window.");
            RequestPopOutWindow?.Invoke(s);
        };

        session.OnFullscreenRequested = (s) =>
        {
            LogEngine.Instance.Info("Session", $"Opening session tab '{s.Title}' in fullscreen window.");
            RequestFullscreenWindow?.Invoke(s);
        };
    }

    [RelayCommand]
    public async Task QuickConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(QuickConnectHost))
            return;

        LogEngine.Instance.Info("Session", $"Quick connect initiated: Protocol={QuickConnectProtocol}, Target='{QuickConnectHost}'");

        var host = QuickConnectHost.Trim();
        var port = QuickConnectProtocol switch
        {
            ProtocolType.RDP => 3389,
            ProtocolType.SSH => 22,
            ProtocolType.VNC => 5900,
            ProtocolType.Web => 443,
            _ => 3389
        };

        // If host contains :port, parse it
        if (host.Contains(':'))
        {
            var parts = host.Split(':');
            host = parts[0];
            if (int.TryParse(parts[1], out var parsedPort))
            {
                port = parsedPort;
            }
        }

        // Check if a connection with the same host, port, and protocol already exists
        var existing = Connections.FirstOrDefault(c =>
            string.Equals(c.Host, host, StringComparison.OrdinalIgnoreCase) &&
            c.Port == port &&
            c.Protocol == QuickConnectProtocol);

        if (existing != null)
        {
            // Reuse the existing saved connection
            await ConnectAsync(existing);
            return;
        }

        // Save as a new connection
        var newConnection = new ConnectionItem
        {
            Name = host,
            Host = host,
            Port = port,
            Protocol = QuickConnectProtocol,
            DisplayMode = DisplayMode.Tabbed,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _databaseService.SaveConnectionAsync(newConnection);
        await LoadDataAsync();
        await ConnectAsync(newConnection);
    }

    [RelayCommand]
    public async Task AddConnectionAsync()
    {
        LogEngine.Instance.Info("UI", "MainViewModel.AddConnectionAsync triggered.");
        if (RequestConnectionEditorPage != null)
        {
            RequestConnectionEditorPage.Invoke(null);
            return;
        }

        if (RequestConnectionEditor == null)
        {
            LogEngine.Instance.Warn("UI", "No ConnectionEditor handler registered for AddConnectionAsync!");
            return;
        }
        var newConn = await RequestConnectionEditor.Invoke(null);
        if (newConn != null)
        {
            await _databaseService.SaveConnectionAsync(newConn);
            await LoadDataAsync();
        }
    }

    [RelayCommand]
    public async Task EditConnectionAsync(object? parameter)
    {
        ConnectionItem? connection = parameter switch
        {
            ConnectionItem conn => conn,
            ConnectionCardViewModel card => card.Model,
            _ => null
        };

        if (connection == null)
        {
            LogEngine.Instance.Warn("UI", $"MainViewModel.EditConnectionAsync called with null or invalid connection parameter: '{parameter}'.");
            return;
        }

        LogEngine.Instance.Info("UI", $"MainViewModel: EditConnectionAsync triggered for '{connection.DisplayName}' (Id: {connection.Id}).");

        if (RequestConnectionEditorPage != null)
        {
            LogEngine.Instance.Debug("UI", $"Invoking RequestConnectionEditorPage for '{connection.DisplayName}'");
            RequestConnectionEditorPage.Invoke(connection);
            return;
        }

        if (RequestConnectionEditor == null)
        {
            LogEngine.Instance.Warn("UI", $"No ConnectionEditor handler registered for EditConnectionAsync on '{connection.DisplayName}'!");
            return;
        }

        LogEngine.Instance.Debug("UI", $"Invoking modal RequestConnectionEditor fallback for '{connection.DisplayName}'");
        var updated = await RequestConnectionEditor.Invoke(connection);
        if (updated != null)
        {
            await _databaseService.SaveConnectionAsync(updated);
            await LoadDataAsync();
        }
    }

    public async Task SaveAndReloadConnectionAsync(ConnectionItem connection)
    {
        await _databaseService.SaveConnectionAsync(connection);
        await LoadDataAsync();
    }

    [RelayCommand]
    public async Task DeleteConnectionAsync(object? parameter)
    {
        ConnectionItem? connection = parameter switch
        {
            ConnectionItem conn => conn,
            ConnectionCardViewModel card => card.Model,
            _ => null
        };

        if (connection == null)
        {
            LogEngine.Instance.Warn("UI", $"MainViewModel.DeleteConnectionAsync called with null or invalid connection parameter: '{parameter}'.");
            return;
        }

        bool confirm;
        if (RequestConfirmWithNameAsync != null)
        {
            confirm = await RequestConfirmWithNameAsync.Invoke(
                "Delete Connection",
                "This action cannot be undone. To confirm, type the connection name below.",
                connection.DisplayName,
                "Delete");
        }
        else if (RequestConfirmationAsync != null)
        {
            confirm = await RequestConfirmationAsync.Invoke(
                "Delete Connection",
                $"Are you sure you want to delete '{connection.Name}'? This action cannot be undone.");
        }
        else
        {
            confirm = RequestConfirmation?.Invoke(
                "Delete Connection",
                $"Are you sure you want to delete '{connection.Name}'?") ?? true;
        }

        if (confirm)
        {
            await _databaseService.DeleteConnectionAsync(connection.Id);
            await LoadDataAsync();
        }
    }

    [RelayCommand]
    public async Task AddCredentialAsync()
    {
        if (RequestCredentialEditorPage != null)
        {
            RequestCredentialEditorPage.Invoke(null);
            return;
        }

        if (RequestCredentialEditor == null) return;
        var newCred = await RequestCredentialEditor.Invoke(null);
        if (newCred != null)
        {
            await _databaseService.SaveCredentialAsync(newCred);
            await LoadDataAsync();
        }
    }

    [RelayCommand]
    public async Task EditCredentialAsync(Credential credential)
    {
        if (RequestCredentialEditorPage != null)
        {
            RequestCredentialEditorPage.Invoke(credential);
            return;
        }

        if (RequestCredentialEditor == null) return;
        var updated = await RequestCredentialEditor.Invoke(credential);
        if (updated != null)
        {
            await _databaseService.SaveCredentialAsync(updated);
            await LoadDataAsync();
        }
    }

    [RelayCommand]
    public async Task DeleteCredentialAsync(Credential credential)
    {
        bool confirm;
        if (RequestConfirmWithNameAsync != null)
        {
            confirm = await RequestConfirmWithNameAsync.Invoke(
                "Delete Credential",
                "Associated connections will no longer have saved credentials. To confirm, type the credential name below.",
                credential.Title,
                "Delete");
        }
        else if (RequestConfirmationAsync != null)
        {
            confirm = await RequestConfirmationAsync.Invoke(
                "Delete Credential",
                $"Are you sure you want to delete credential '{credential.Title}'? Associated connections will no longer have saved credentials.");
        }
        else
        {
            confirm = RequestConfirmation?.Invoke(
                "Delete Credential",
                $"Are you sure you want to delete credential '{credential.Title}'?") ?? true;
        }

        if (confirm)
        {
            await _databaseService.DeleteCredentialAsync(credential.Id);
            await LoadDataAsync();
        }
    }

    public async Task SaveAndReloadCredentialAsync(Credential credential)
    {
        await _databaseService.SaveCredentialAsync(credential);
        await LoadDataAsync();
    }

    [RelayCommand]
    public async Task SaveSettingsAsync()
    {
        await _databaseService.SaveSettingsAsync(Settings);
        LogEngine.Instance.Configure(Settings);
    }

    public string GetCredentialTitle(Guid? credentialId)
    {
        if (!credentialId.HasValue) return "None";
        var cred = Credentials.FirstOrDefault(c => c.Id == credentialId.Value);
        return cred != null ? cred.Title : "Unknown";
    }
}
