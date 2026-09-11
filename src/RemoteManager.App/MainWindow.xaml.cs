using System.IO;
using System.Windows;
using System.Windows.Controls;
using RemoteManager.App.ViewModels;
using RemoteManager.App.Views;
using RemoteManager.Core.Logging;
using RemoteManager.Core.Models;
using RemoteManager.Data;
using RemoteManager.Data.Security;
using RemoteManager.Protocols.Vnc;

namespace RemoteManager.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        var db = new SqliteDatabaseService();
        var crypto = new DpapiEncryptionService();
        _viewModel = new MainViewModel(db, crypto);
        DataContext = _viewModel;

        _viewModel.ActiveSessions.CollectionChanged += (s, e) =>
        {
            if (_viewModel.ActiveSessions.Count == 0 && SessionsAndDashboardArea.Visibility == Visibility.Visible)
            {
                if (NavConnectionsItem != null) NavConnectionsItem.IsActive = true;
            }
            else if (_viewModel.ActiveSessions.Count > 0 && SessionsAndDashboardArea.Visibility == Visibility.Visible)
            {
                if (NavConnectionsItem != null) NavConnectionsItem.IsActive = false;
            }
        };

        SetupDialogHandlers(crypto);

        Loaded += async (s, e) =>
        {
            try
            {
                if (_viewModel.ActiveSessions.Count == 0 && NavConnectionsItem != null)
                {
                    NavConnectionsItem.IsActive = true;
                }
                LogEngine.Instance.Info("App", "MainWindow loaded, initializing data...");
                await _viewModel.InitializeAsync();
                LogEngine.Instance.Info("App", "MainWindow and database initialized successfully.");
                if (_viewModel.ActiveSessions.Count == 0 && NavConnectionsItem != null)
                {
                    NavConnectionsItem.IsActive = true;
                }
            }
            catch (Exception ex)
            {
                LogEngine.Instance.Error("App", "Error during MainWindow initialization", ex);
            }
        };

        Closing += (s, e) =>
        {
            LogEngine.Instance.Info("App", "MainWindow closing...");
        };

        Closed += (s, e) =>
        {
            LogEngine.Instance.Info("App", "MainWindow closed.");
        };
    }

    private Action? _previousViewNavigator;

    private void SetupDialogHandlers(DpapiEncryptionService crypto)
    {
        // 1. Connection Editor: Full Page (Zero Modal)
        _viewModel.RequestConnectionEditorPage = (existing) =>
        {
            NavigateToConnectionEdit(existing, crypto);
        };

        // 2. Credential Editor: Full Page (Zero Modal)
        _viewModel.RequestCredentialEditorPage = (existing) =>
        {
            NavigateToCredentialEdit(existing, crypto);
        };

        // 3. Simple Confirmation (e.g., close tab / disconnect)
        _viewModel.RequestConfirmationAsync = (title, message) =>
        {
            return ConfirmationDialog.ShowSimpleAsync(this, title, message, "Confirm", isDanger: true);
        };

        // 4. Confirm-with-Name (e.g., delete connection / credential)
        _viewModel.RequestConfirmWithNameAsync = (title, message, itemName, actionText) =>
        {
            return ConfirmationDialog.ShowConfirmWithNameAsync(this, title, message, itemName, actionText);
        };

        // Fallbacks if ever called
        _viewModel.RequestConfirmation = (title, message) =>
        {
            return ConfirmationDialog.ShowSimpleAsync(this, title, message, "Confirm", isDanger: true).GetAwaiter().GetResult();
        };

        // Wire ConnectionEditControl events
        ConnectionEditControl.ConnectionSaved += async (savedItem, inlineCred, andConnect) =>
        {
            if (inlineCred != null)
            {
                await _viewModel.SaveAndReloadCredentialAsync(inlineCred);
                savedItem.CredentialId = inlineCred.Id;
            }
            await _viewModel.SaveAndReloadConnectionAsync(savedItem);
            ReturnToPreviousView();
            if (andConnect)
            {
                await _viewModel.ConnectAsync(savedItem);
            }
        };
        ConnectionEditControl.CancelRequested += () =>
        {
            ReturnToPreviousView();
        };

        // Wire CredentialEditControl events
        CredentialEditControl.CredentialSaved += async (savedCred) =>
        {
            await _viewModel.SaveAndReloadCredentialAsync(savedCred);
            ReturnToPreviousView();
        };
        CredentialEditControl.CancelRequested += () =>
        {
            ReturnToPreviousView();
        };

        // Keep SessionContentPresenter.Content synchronized with _viewModel.SelectedSession
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(_viewModel.SelectedSession))
            {
                SessionContentPresenter.Content = _viewModel.SelectedSession?.Content;
                if (_viewModel.SelectedSession != null)
                {
                    ReturnToWorkspace();
                }
                if (_viewModel.SelectedSession?.Content is VncHostControl vnc)
                {
                    vnc.RefreshDesktop();
                }
            }
        };

        _viewModel.RequestPopOutWindow = (session) =>
        {
            SessionContentPresenter.Content = null;
            _viewModel.ActiveSessions.Remove(session);
            if (_viewModel.SelectedSession == session)
            {
                _viewModel.SelectedSession = _viewModel.ActiveSessions.LastOrDefault();
            }

            var detached = new DetachedSessionWindow(session, (restoredSession) =>
            {
                if (!_viewModel.ActiveSessions.Contains(restoredSession))
                {
                    _viewModel.ActiveSessions.Add(restoredSession);
                }
                _viewModel.SelectedSession = restoredSession;
                SessionContentPresenter.Content = restoredSession.Content;
                if (restoredSession.Content is VncHostControl vnc)
                {
                    vnc.RefreshDesktop();
                }
            });
            detached.Show();
        };

        _viewModel.RequestFullscreenWindow = (session) =>
        {
            SessionContentPresenter.Content = null;
            _viewModel.ActiveSessions.Remove(session);
            if (_viewModel.SelectedSession == session)
            {
                _viewModel.SelectedSession = _viewModel.ActiveSessions.LastOrDefault();
            }

            var fullscreen = new FullscreenSessionWindow(session, (restoredSession) =>
            {
                if (!_viewModel.ActiveSessions.Contains(restoredSession))
                {
                    _viewModel.ActiveSessions.Add(restoredSession);
                }
                _viewModel.SelectedSession = restoredSession;
                SessionContentPresenter.Content = restoredSession.Content;
                if (restoredSession.Content is VncHostControl vnc)
                {
                    vnc.RefreshDesktop();
                }
            });
            fullscreen.Show();
        };
    }

    private void HideAllPages()
    {
        SessionsAndDashboardArea.Visibility = Visibility.Collapsed;
        SettingsPageArea.Visibility = Visibility.Collapsed;
        AboutPageArea.Visibility = Visibility.Collapsed;
        LogsPageArea.Visibility = Visibility.Collapsed;
        ConnectionsPageArea.Visibility = Visibility.Collapsed;
        ConnectionEditPageArea.Visibility = Visibility.Collapsed;
        CredentialEditPageArea.Visibility = Visibility.Collapsed;

        if (NavConnectionsItem != null) NavConnectionsItem.IsActive = false;
        if (NavLogsItem != null) NavLogsItem.IsActive = false;
        if (NavSettingsItem != null) NavSettingsItem.IsActive = false;
        if (NavAboutItem != null) NavAboutItem.IsActive = false;
    }

    public void NavigateToConnectionEdit(ConnectionItem? existing, DpapiEncryptionService crypto)
    {
        if (HasActiveSessions())
        {
            var title = existing != null ? $"Edit: {existing.Name}" : "New Connection";
            var tag = existing != null ? $"EditConn_{existing.Id}" : "NewConnection";
            OpenAsUtilityTab(title, tag, () =>
            {
                var ctrl = new ConnectionEditView();
                ctrl.LoadConnection(existing, _viewModel.Credentials, crypto);
                ctrl.ConnectionSaved += async (savedItem, inlineCred, andConnect) =>
                {
                    if (inlineCred != null)
                    {
                        await _viewModel.SaveAndReloadCredentialAsync(inlineCred);
                        savedItem.CredentialId = inlineCred.Id;
                    }
                    await _viewModel.SaveAndReloadConnectionAsync(savedItem);
                    CloseUtilityTab(tag);
                    if (andConnect)
                    {
                        await _viewModel.ConnectAsync(savedItem);
                    }
                };
                ctrl.CancelRequested += () =>
                {
                    CloseUtilityTab(tag);
                };
                return ctrl;
            });
            return;
        }

        // Capture current view as previous
        if (ConnectionsPageArea.Visibility == Visibility.Visible)
            _previousViewNavigator = () => OnNavConnectionsClick(this, new RoutedEventArgs());
        else if (LogsPageArea.Visibility == Visibility.Visible)
            _previousViewNavigator = () => OnNavLogsClick(this, new RoutedEventArgs());
        else if (SettingsPageArea.Visibility == Visibility.Visible)
            _previousViewNavigator = () => OnNavSettingsClick(this, new RoutedEventArgs());
        else
            _previousViewNavigator = () => ReturnToWorkspace();

        HideAllPages();
        ConnectionEditControl.LoadConnection(existing, _viewModel.Credentials, crypto);
        ConnectionEditPageArea.Visibility = Visibility.Visible;
        LogEngine.Instance.Info("UI", $"Navigated to Connection Edit page (Editing: {existing?.Name ?? "New"}).");
    }

    public void NavigateToCredentialEdit(Credential? existing, DpapiEncryptionService crypto)
    {
        if (HasActiveSessions())
        {
            var title = existing != null ? $"Edit: {existing.Title}" : "New Credential";
            var tag = existing != null ? $"EditCred_{existing.Id}" : "NewCredential";
            OpenAsUtilityTab(title, tag, () =>
            {
                var ctrl = new CredentialEditView();
                ctrl.LoadCredential(existing, crypto);
                ctrl.CredentialSaved += async (savedCred) =>
                {
                    await _viewModel.SaveAndReloadCredentialAsync(savedCred);
                    CloseUtilityTab(tag);
                };
                ctrl.CancelRequested += () =>
                {
                    CloseUtilityTab(tag);
                };
                return ctrl;
            });
            return;
        }

        if (ConnectionsPageArea.Visibility == Visibility.Visible)
            _previousViewNavigator = () => OnNavConnectionsClick(this, new RoutedEventArgs());
        else
            _previousViewNavigator = () => ReturnToWorkspace();

        HideAllPages();
        CredentialEditControl.LoadCredential(existing, crypto);
        CredentialEditPageArea.Visibility = Visibility.Visible;
        LogEngine.Instance.Info("UI", $"Navigated to Credential Edit page (Editing: {existing?.Title ?? "New"}).");
    }

    private void ReturnToPreviousView()
    {
        if (_previousViewNavigator != null)
        {
            var nav = _previousViewNavigator;
            _previousViewNavigator = null;
            nav.Invoke();
        }
        else
        {
            ReturnToWorkspace();
        }
    }

    public Task<bool> ShowConfirmationAsync(string title, string message)
    {
        return ConfirmationDialog.ShowSimpleAsync(this, title, message, "Confirm", isDanger: true);
    }

    private void OnSidebarTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != SidebarTabControl) return;

        ReturnToWorkspace();
    }

    private void OnSidebarTabPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ReturnToWorkspace();
    }

    private void ReturnToWorkspace()
    {
        HideAllPages();
        SessionsAndDashboardArea.Visibility = Visibility.Visible;
        if (_viewModel?.ActiveSessions.Count == 0 && NavConnectionsItem != null)
        {
            NavConnectionsItem.IsActive = true;
        }
    }

    private void OnNavConnectionsClick(object sender, RoutedEventArgs e)
    {
        OpenAsUtilityTab("Connections", "Connections", () =>
        {
            var view = new ConnectionsView();
            view.DataContext = _viewModel.ConnectionsVM;
            return view;
        });

        if (NavConnectionsItem != null) NavConnectionsItem.IsActive = true;
        LogEngine.Instance.Info("UI", "Navigated to Connections tab.");
    }

    private void OnNavLogsClick(object sender, RoutedEventArgs e)
    {
        if (HasActiveSessions())
        {
            OpenAsUtilityTab("Logs", "Logs", () => new LogsView());
            return;
        }

        HideAllPages();
        LogsPageArea.Visibility = Visibility.Visible;

        if (NavLogsItem != null) NavLogsItem.IsActive = true;
        LogEngine.Instance.Info("UI", "Navigated to Logs page.");
    }

    private void OnNavSettingsClick(object sender, RoutedEventArgs e)
    {
        if (HasActiveSessions())
        {
            OpenAsUtilityTab("Settings", "Settings", () => new SettingsView());
            return;
        }

        HideAllPages();
        SettingsPageArea.Visibility = Visibility.Visible;

        if (NavSettingsItem != null) NavSettingsItem.IsActive = true;
        LogEngine.Instance.Info("UI", "Navigated to Settings page.");
    }

    private void OnNavAboutClick(object sender, RoutedEventArgs e)
    {
        if (HasActiveSessions())
        {
            OpenAsUtilityTab("About", "About", () => new AboutView());
            return;
        }

        HideAllPages();
        AboutPageArea.Visibility = Visibility.Visible;

        if (NavAboutItem != null) NavAboutItem.IsActive = true;
        LogEngine.Instance.Info("UI", "Navigated to About page.");
    }

    /// <summary>
    /// Returns true if there are any active tabs (remote sessions or utility tabs).
    /// </summary>
    private bool HasActiveSessions()
    {
        return _viewModel.ActiveSessions.Count > 0;
    }

    private void CloseUtilityTab(string tag)
    {
        var tab = _viewModel.ActiveSessions.FirstOrDefault(s => s.IsUtilityTab && s.UtilityTabTag == tag);
        if (tab != null)
        {
            _viewModel.ActiveSessions.Remove(tab);
            if (_viewModel.SelectedSession == tab)
            {
                _viewModel.SelectedSession = _viewModel.ActiveSessions.LastOrDefault();
            }
        }
    }

    /// <summary>
    /// Opens a page as a utility tab in the session tab bar.
    /// If a tab with the same tag already exists, selects it instead of creating a duplicate.
    /// </summary>
    private void OpenAsUtilityTab(string title, string tag, Func<System.Windows.Controls.UserControl> viewFactory)
    {
        // Reuse existing tab if already open
        var existing = _viewModel.ActiveSessions.FirstOrDefault(s => s.IsUtilityTab && s.UtilityTabTag == tag);
        if (existing != null)
        {
            _viewModel.SelectedSession = existing;
            LogEngine.Instance.Debug("UI", $"Utility tab '{tag}' already open, switching to it.");
            return;
        }

        var view = viewFactory();
        var tab = new SessionTabViewModel(title, tag, view);

        // Utility tabs close without disconnect confirmation
        tab.OnCloseRequested = async (s) =>
        {
            LogEngine.Instance.Info("UI", $"Closing utility tab '{s.UtilityTabTag}'.");
            _viewModel.ActiveSessions.Remove(s);
            if (_viewModel.SelectedSession == s)
            {
                _viewModel.SelectedSession = _viewModel.ActiveSessions.LastOrDefault();
            }
        };

        _viewModel.ActiveSessions.Add(tab);
        _viewModel.SelectedSession = tab;

        // Ensure sessions area is visible
        ReturnToWorkspace();

        LogEngine.Instance.Info("UI", $"Opened '{title}' as utility tab (sessions active).");
    }

    private double _previousSidebarWidth = 340;
    private bool _isSidebarCollapsed = false;

    private void OnToggleSidebarClick(object sender, RoutedEventArgs e)
    {
        ToggleSidebar();
    }

    public void ToggleSidebar()
    {
        _isSidebarCollapsed = !_isSidebarCollapsed;

        if (_isSidebarCollapsed)
        {
            if (SidebarColumn.ActualWidth > 100)
            {
                _previousSidebarWidth = SidebarColumn.ActualWidth;
            }
            SidebarExpandedContent.Visibility = Visibility.Collapsed;
            SidebarCollapsedContent.Visibility = Visibility.Visible;
            SidebarColumn.MinWidth = 48;
            SidebarColumn.MaxWidth = 48;
            SidebarColumn.Width = new GridLength(48);
            SplitterColumn.Width = new GridLength(0);
            MainGridSplitter.Visibility = Visibility.Collapsed;
            LogEngine.Instance.Debug("UI", "Nav sidebar collapsed to compact icon rail.");
        }
        else
        {
            SidebarCollapsedContent.Visibility = Visibility.Collapsed;
            SidebarExpandedContent.Visibility = Visibility.Visible;
            SidebarColumn.MaxWidth = 500;
            SidebarColumn.MinWidth = 260;
            SidebarColumn.Width = new GridLength(_previousSidebarWidth > 100 ? _previousSidebarWidth : 340);
            SplitterColumn.Width = new GridLength(2);
            MainGridSplitter.Visibility = Visibility.Visible;
            LogEngine.Instance.Debug("UI", "Nav sidebar expanded.");
        }
    }

    private void OnCollapsedServersClick(object sender, RoutedEventArgs e)
    {
        SidebarTabControl.SelectedItem = ServersTab;
        if (_isSidebarCollapsed)
        {
            ToggleSidebar();
        }
    }

    private void OnCollapsedVaultClick(object sender, RoutedEventArgs e)
    {
        SidebarTabControl.SelectedItem = VaultTab;
        if (_isSidebarCollapsed)
        {
            ToggleSidebar();
        }
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == System.Windows.Input.Key.B && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
        {
            ToggleSidebar();
            e.Handled = true;
        }
    }
}