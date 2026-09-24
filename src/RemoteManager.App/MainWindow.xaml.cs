using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RemoteManager.App.ViewModels;
using RemoteManager.App.Views;
using RemoteManager.Core.Logging;
using RemoteManager.Core.Models;
using RemoteManager.Data;
using RemoteManager.Data.Security;
using RemoteManager.Protocols.Rdp;
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
            UpdateSidebarNavHighlight(_viewModel.SelectedSession);
        };

        SetupDialogHandlers(crypto);
        _viewModel.RequestNavigateToAbout = NavigateToAbout;

        Loaded += async (s, e) =>
        {
            try
            {
                LogEngine.Instance.Info("App", "MainWindow loaded, initializing data...");
                await _viewModel.InitializeAsync();
                LogEngine.Instance.Info("App", "MainWindow and database initialized successfully.");

                OpenStartupTabs();
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

        // 5. Export Dialog
        _viewModel.RequestExportDialog = (selectedIds) =>
        {
            var dlg = new ExportDialog(_viewModel.ExportImportService, selectedIds) { Owner = this };
            var res = dlg.ShowDialog() == true;
            if (res && dlg.WasExportSuccessful && !string.IsNullOrEmpty(dlg.ExportedFilePath))
            {
                MessageBox.Show($"Data successfully exported to:\n{dlg.ExportedFilePath}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return Task.FromResult(res);
        };

        // 6. Import Dialog
        _viewModel.RequestImportDialog = async () =>
        {
            var dlg = new ImportDialog(_viewModel.ExportImportService) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.WasImportSuccessful && dlg.Result != null)
            {
                await _viewModel.LoadDataAsync();
                MessageBox.Show(dlg.Result.SummaryText, "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            return false;
        };

        // 7. Save File Dialog for RDP export
        _viewModel.RequestSaveFileDialog = (defaultName) =>
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export RDP File",
                Filter = "Remote Desktop Connection (*.rdp)|*.rdp|All Files (*.*)|*.*",
                FileName = defaultName,
                DefaultExt = ".rdp"
            };
            return Task.FromResult(sfd.ShowDialog(this) == true ? sfd.FileName : (string?)null);
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
                UpdateSidebarNavHighlight(_viewModel.SelectedSession);
                if (_viewModel.SelectedSession?.Content is IRdpHostControl rdp)
                {
                    Dispatcher.BeginInvoke(new Action(() => rdp.FocusRdp()), System.Windows.Threading.DispatcherPriority.Input);
                }
                else if (_viewModel.SelectedSession?.Content is VncHostControl vnc)
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
                if (restoredSession.Content is IRdpHostControl restoredRdp)
                {
                    Dispatcher.BeginInvoke(new Action(() => restoredRdp.FocusRdp()), System.Windows.Threading.DispatcherPriority.Input);
                }
                else if (restoredSession.Content is VncHostControl vnc)
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
                if (restoredSession.Content is IRdpHostControl restoredRdp)
                {
                    Dispatcher.BeginInvoke(new Action(() => restoredRdp.FocusRdp()), System.Windows.Threading.DispatcherPriority.Input);
                }
                else if (restoredSession.Content is VncHostControl vnc)
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
        LogEngine.Instance.Info("UI", $"NavigateToConnectionEdit called (Existing: '{existing?.DisplayName ?? "New"}').");
        try
        {
            var title = existing != null ? $"Edit: {existing.DisplayName}" : "New Connection";
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
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("UI", $"Error navigating to Connection Edit page for '{existing?.DisplayName ?? "New"}'", ex);
        }
    }

    public void NavigateToCredentialEdit(Credential? existing, DpapiEncryptionService crypto)
    {
        LogEngine.Instance.Info("UI", $"NavigateToCredentialEdit called (Existing: '{existing?.Title ?? "New"}').");
        try
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
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("UI", $"Error navigating to Credential Edit page for '{existing?.Title ?? "New"}'", ex);
        }
    }

    private void ReturnToPreviousView()
    {
        ReturnToWorkspace();
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
        DependencyObject? current = e.OriginalSource as DependencyObject;
        while (current != null && current != SidebarTabControl)
        {
            if (current is TabItem or TabPanel)
            {
                ReturnToWorkspace();
                return;
            }
            current = VisualTreeHelper.GetParent(current);
        }
    }

    private void ReturnToWorkspace()
    {
        HideAllPages();
        SessionsAndDashboardArea.Visibility = Visibility.Visible;
        UpdateSidebarNavHighlight(_viewModel?.SelectedSession);
    }

    private void UpdateSidebarNavHighlight(SessionTabViewModel? session)
    {
        bool isConn = session != null && session.IsUtilityTab && session.UtilityTabTag == "Connections";
        bool isLogs = session != null && session.IsUtilityTab && session.UtilityTabTag == "Logs";
        bool isSettings = session != null && session.IsUtilityTab && session.UtilityTabTag == "Settings";
        bool isAbout = session != null && session.IsUtilityTab && session.UtilityTabTag == "About";

        if (session == null && (_viewModel == null || _viewModel.ActiveSessions.Count == 0))
        {
            isConn = true;
        }

        if (NavConnectionsItem != null) NavConnectionsItem.IsActive = isConn;
        if (NavLogsItem != null) NavLogsItem.IsActive = isLogs;
        if (NavSettingsItem != null) NavSettingsItem.IsActive = isSettings;
        if (NavAboutItem != null) NavAboutItem.IsActive = isAbout;
    }

    /// <summary>
    /// Opens the Connections Hub as a tab on application startup.
    /// </summary>
    public void OpenStartupTabs()
    {
        OpenAsUtilityTab("Connections", "Connections", () =>
        {
            var view = new ConnectionsView();
            view.DataContext = _viewModel.ConnectionsVM;
            return view;
        }, selectTab: true);

        UpdateSidebarNavHighlight(_viewModel.SelectedSession);
    }

    private void OnNavConnectionsClick(object sender, RoutedEventArgs e)
    {
        OpenAsUtilityTab("Connections", "Connections", () =>
        {
            var view = new ConnectionsView();
            view.DataContext = _viewModel.ConnectionsVM;
            return view;
        });

        UpdateSidebarNavHighlight(_viewModel.SelectedSession);
        LogEngine.Instance.Info("UI", "Navigated to Connections tab.");
    }

    private void OnNavLogsClick(object sender, RoutedEventArgs e)
    {
        OpenAsUtilityTab("Logs", "Logs", () => new LogsView());
        UpdateSidebarNavHighlight(_viewModel.SelectedSession);
        LogEngine.Instance.Info("UI", "Navigated to Logs tab.");
    }

    private void OnNavSettingsClick(object sender, RoutedEventArgs e)
    {
        OpenAsUtilityTab("Settings", "Settings", () => new SettingsView());
        UpdateSidebarNavHighlight(_viewModel.SelectedSession);
        LogEngine.Instance.Info("UI", "Navigated to Settings tab.");
    }

    public void NavigateToAbout()
    {
        OpenAsUtilityTab("About", "About", () => new AboutView { DataContext = _viewModel });
        UpdateSidebarNavHighlight(_viewModel.SelectedSession);
        LogEngine.Instance.Info("UI", "Navigated to About tab.");
    }

    private void OnNavAboutClick(object sender, RoutedEventArgs e)
    {
        NavigateToAbout();
    }

    private void OnFooterUpdateStatusClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        NavigateToAbout();
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
    private void OpenAsUtilityTab(string title, string tag, Func<System.Windows.Controls.UserControl> viewFactory, bool selectTab = true)
    {
        // Reuse existing tab if already open
        var existing = _viewModel.ActiveSessions.FirstOrDefault(s => s.IsUtilityTab && s.UtilityTabTag == tag);
        if (existing != null)
        {
            if (selectTab)
            {
                _viewModel.SelectedSession = existing;
            }
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
        if (selectTab)
        {
            _viewModel.SelectedSession = tab;
        }

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

    #region Sidebar Drag and Drop Sorting
    private System.Windows.Point _sidebarDragStartPoint;
    private ConnectionItem? _sidebarDraggedItem;

    private void OnSidebarItemPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (IsInteractiveControl(e.OriginalSource as DependencyObject))
        {
            _sidebarDraggedItem = null;
            return;
        }

        _sidebarDragStartPoint = e.GetPosition(null);
        _sidebarDraggedItem = FindItemFromVisualTree<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as ConnectionItem;
    }

    private void OnSidebarItemMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || _sidebarDraggedItem == null)
            return;

        System.Windows.Point currentPos = e.GetPosition(null);
        System.Windows.Vector diff = _sidebarDragStartPoint - currentPos;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            var itemToDrag = _sidebarDraggedItem;
            _sidebarDraggedItem = null;
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject("RemoteManager.ConnectionItem", itemToDrag), DragDropEffects.Move);
        }
    }

    private void OnSidebarItemDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("RemoteManager.ConnectionItem"))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private async void OnSidebarItemDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("RemoteManager.ConnectionItem"))
            return;

        var sourceItem = e.Data.GetData("RemoteManager.ConnectionItem") as ConnectionItem;
        if (sourceItem == null) return;

        var targetListBoxItem = FindItemFromVisualTree<ListBoxItem>(e.OriginalSource as DependencyObject);
        var targetItem = targetListBoxItem?.DataContext as ConnectionItem;

        if (targetItem != null && targetItem.Id != sourceItem.Id)
        {
            await _viewModel.ReorderConnectionItemAsync(sourceItem, targetItem);
        }
    }

    private static T? FindItemFromVisualTree<T>(DependencyObject? dep) where T : DependencyObject
    {
        while (dep != null)
        {
            if (dep is T match) return match;
            if (dep is Visual || dep is System.Windows.Media.Media3D.Visual3D)
            {
                dep = VisualTreeHelper.GetParent(dep);
            }
            else
            {
                dep = LogicalTreeHelper.GetParent(dep);
            }
        }
        return null;
    }

    private static bool IsInteractiveControl(DependencyObject? dep)
    {
        while (dep != null)
        {
            if (dep is ButtonBase || dep is TextBox || dep is PasswordBox || dep is ComboBox)
                return true;
            if (dep is Visual || dep is System.Windows.Media.Media3D.Visual3D)
            {
                dep = VisualTreeHelper.GetParent(dep);
            }
            else
            {
                dep = LogicalTreeHelper.GetParent(dep);
            }
        }
        return false;
    }
    #endregion
}