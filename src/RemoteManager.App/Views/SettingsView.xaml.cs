using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RemoteManager.App.ViewModels;
using RemoteManager.Core.Models;
using RemoteManager.Protocols.Ssh;
using RemoteManager.Protocols.Vnc;
using Wpf.Ui.Appearance;

namespace RemoteManager.App.Views;

public partial class SettingsView : UserControl
{
    private MainViewModel? _viewModel;
    private bool _isLoaded;

    public SettingsView()
    {
        InitializeComponent();

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbPath = Path.Combine(appData, "RemoteManager", "remotemanager.db");
        DbPathTextBlock.Text = dbPath;

        DataContextChanged += (s, e) =>
        {
            if (DataContext is MainViewModel vm)
            {
                _viewModel = vm;
                LoadSettings(vm.Settings);
            }
        };
    }

    private void LoadSettings(AppSettings settings)
    {
        _isLoaded = false;

        // 1. Set Theme ComboBox
        foreach (ComboBoxItem item in ThemeComboBox.Items)
        {
            if (item.Tag?.ToString() == settings.Theme)
            {
                ThemeComboBox.SelectedItem = item;
                break;
            }
        }

        // 2. Set Default Display Mode
        foreach (ComboBoxItem item in DefaultModeComboBox.Items)
        {
            if (item.Tag?.ToString() == settings.DefaultDisplayMode.ToString())
            {
                DefaultModeComboBox.SelectedItem = item;
                break;
            }
        }

        AutoReconnectCheckBox.IsChecked = settings.AutoReconnect;
        WarnDisconnectCheckBox.IsChecked = settings.WarnBeforeDisconnect;

        // 3. Populate SSH Clients
        PopulateSshClients(settings.SshClientType);

        // 4. Custom SSH Executable Fields
        CustomSshPathTextBox.Text = settings.CustomSshClientPath ?? string.Empty;
        CustomSshArgsTextBox.Text = string.IsNullOrEmpty(settings.CustomSshClientArgs)
            ? "{user}@{host} -p {port}"
            : settings.CustomSshClientArgs;

        UpdateSshPanelState(SshClientComboBox.SelectedItem as SshClientInfo);

        // 5. Populate VNC Clients
        PopulateVncClients(settings.VncClientType);

        // 6. Custom VNC Executable Fields
        CustomVncPathTextBox.Text = settings.CustomVncClientPath ?? string.Empty;
        CustomVncArgsTextBox.Text = string.IsNullOrEmpty(settings.CustomVncClientArgs)
            ? "{host}:{port}"
            : settings.CustomVncClientArgs;

        UpdateVncPanelState(VncClientComboBox.SelectedItem as VncClientInfo);

        // 7. Populate Logging Configuration
        LoggingEnabledSwitch.IsChecked = settings.IsLoggingEnabled;
        foreach (ComboBoxItem item in MinLogLevelComboBox.Items)
        {
            if (string.Equals(item.Tag?.ToString(), settings.MinimumLogLevel, StringComparison.OrdinalIgnoreCase))
            {
                MinLogLevelComboBox.SelectedItem = item;
                break;
            }
        }
        if (MinLogLevelComboBox.SelectedItem == null && MinLogLevelComboBox.Items.Count > 1)
        {
            MinLogLevelComboBox.SelectedIndex = 1; // Default Debug
        }

        LogAppSwitch.IsChecked = settings.LogAppLifecycle;
        LogDbSwitch.IsChecked = settings.LogDatabase;
        LogRdpSwitch.IsChecked = settings.LogRdp;
        LogSshSwitch.IsChecked = settings.LogSsh;
        LogVncSwitch.IsChecked = settings.LogVnc;
        LogWebSwitch.IsChecked = settings.LogWeb;
        LogSecuritySwitch.IsChecked = settings.LogSecurity;

        ModuleOptionsPanel.IsEnabled = settings.IsLoggingEnabled;
        LogLevelContainerGrid.IsEnabled = settings.IsLoggingEnabled;

        _isLoaded = true;
    }

    private void PopulateSshClients(string selectedId)
    {
        var clients = SshClientDetector.DetectAvailableClients();
        SshClientComboBox.ItemsSource = clients;

        var matching = clients.FirstOrDefault(c => string.Equals(c.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                       ?? clients.FirstOrDefault(c => c.Id == "Auto")
                       ?? clients.FirstOrDefault();

        if (matching != null)
        {
            SshClientComboBox.SelectedItem = matching;
        }
    }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded) return;

        if (ThemeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string themeStr)
        {
            if (_viewModel != null)
            {
                _viewModel.Settings.Theme = themeStr;
                _ = _viewModel.SaveSettingsAsync();
            }

            try
            {
                if (themeStr == "Dark")
                {
                    ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                }
                else if (themeStr == "Light")
                {
                    ApplicationThemeManager.Apply(ApplicationTheme.Light);
                }
                else
                {
                    ApplicationThemeManager.ApplySystemTheme();
                }
            }
            catch { }
        }
    }

    private void OnDefaultModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded) return;

        if (DefaultModeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string modeStr)
        {
            if (_viewModel != null && Enum.TryParse<DisplayMode>(modeStr, out var mode))
            {
                _viewModel.Settings.DefaultDisplayMode = mode;
                _ = _viewModel.SaveSettingsAsync();
            }
        }
    }

    private void OnSettingToggled(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;

        if (_viewModel != null)
        {
            _viewModel.Settings.AutoReconnect = AutoReconnectCheckBox.IsChecked ?? false;
            _viewModel.Settings.WarnBeforeDisconnect = WarnDisconnectCheckBox.IsChecked ?? true;
            _ = _viewModel.SaveSettingsAsync();
        }
    }

    private void OnSshClientSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded) return;

        if (SshClientComboBox.SelectedItem is SshClientInfo client)
        {
            if (_viewModel != null)
            {
                _viewModel.Settings.SshClientType = client.Id;
                _ = _viewModel.SaveSettingsAsync();
            }

            UpdateSshPanelState(client);
        }
    }

    private void UpdateSshPanelState(SshClientInfo? client)
    {
        if (client == null) return;

        if (client.Id == "Custom")
        {
            CustomSshPanel.Visibility = Visibility.Visible;
            CustomSshHeaderTextBlock.Text = "Custom Client Executable Path";
            ClientNotDetectedBanner.Visibility = Visibility.Collapsed;
        }
        else if (!client.IsInstalled && client.Id != "Auto")
        {
            CustomSshPanel.Visibility = Visibility.Visible;
            CustomSshHeaderTextBlock.Text = $"{client.DisplayName} Executable Path";
            ClientNotDetectedBannerText.Text = $"'{client.DisplayName}' was not detected in standard system locations. Please specify the executable path.";
            ClientNotDetectedBanner.Visibility = Visibility.Visible;
        }
        else
        {
            CustomSshPanel.Visibility = Visibility.Collapsed;
            ClientNotDetectedBanner.Visibility = Visibility.Collapsed;
        }
    }

    private void OnRescanSshClientsClick(object sender, RoutedEventArgs e)
    {
        var currentId = (_viewModel?.Settings.SshClientType) ?? "Auto";
        var clients = SshClientDetector.DetectAvailableClients();
        SshClientComboBox.ItemsSource = clients;

        var matching = clients.FirstOrDefault(c => string.Equals(c.Id, currentId, StringComparison.OrdinalIgnoreCase))
                       ?? clients.FirstOrDefault(c => c.Id == "Auto")
                       ?? clients.FirstOrDefault();

        if (matching != null)
        {
            SshClientComboBox.SelectedItem = matching;
        }

        var detectedCount = clients.Count(c => c.IsInstalled && c.Id != "Auto" && c.Id != "Custom");
        ScanResultTextBlock.Text = $"✓ Found {detectedCount} installed client(s)";
        ScanResultTextBlock.Visibility = Visibility.Visible;
    }

    private void OnBrowseCustomSshClick(object sender, RoutedEventArgs e)
    {
        var client = SshClientComboBox.SelectedItem as SshClientInfo;
        var title = client != null && client.Id != "Custom"
            ? $"Locate {client.DisplayName} Executable"
            : "Select Custom SSH Client Executable";

        var dlg = new OpenFileDialog
        {
            Title = title,
            Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dlg.ShowDialog() == true)
        {
            CustomSshPathTextBox.Text = dlg.FileName;
            if (_viewModel != null)
            {
                _viewModel.Settings.CustomSshClientPath = dlg.FileName;
                _ = _viewModel.SaveSettingsAsync();
            }

            if (client != null && !client.IsInstalled)
            {
                client.ExecutablePath = dlg.FileName;
                client.IsInstalled = true;
                ClientNotDetectedBanner.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void OnCustomSshPathChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoaded || _viewModel == null) return;

        _viewModel.Settings.CustomSshClientPath = CustomSshPathTextBox.Text;
        _ = _viewModel.SaveSettingsAsync();
    }

    private void OnCustomSshArgsChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoaded || _viewModel == null) return;

        _viewModel.Settings.CustomSshClientArgs = CustomSshArgsTextBox.Text;
        _ = _viewModel.SaveSettingsAsync();
    }

    private void PopulateVncClients(string selectedId)
    {
        var clients = VncSessionHandler.DetectAvailableClients();
        VncClientComboBox.ItemsSource = clients;

        var matching = clients.FirstOrDefault(c => string.Equals(c.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                       ?? clients.FirstOrDefault(c => c.Id == "BuiltIn")
                       ?? clients.FirstOrDefault();

        if (matching != null)
        {
            VncClientComboBox.SelectedItem = matching;
        }
    }

    private void OnVncClientSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded) return;

        if (VncClientComboBox.SelectedItem is VncClientInfo client)
        {
            if (_viewModel != null)
            {
                _viewModel.Settings.VncClientType = client.Id;
                _ = _viewModel.SaveSettingsAsync();
            }

            UpdateVncPanelState(client);
        }
    }

    private void UpdateVncPanelState(VncClientInfo? client)
    {
        if (client == null) return;

        if (client.Id == "Custom")
        {
            CustomVncPanel.Visibility = Visibility.Visible;
            CustomVncHeaderTextBlock.Text = "Custom VNC Executable Path";
            VncClientNotDetectedBanner.Visibility = Visibility.Collapsed;
        }
        else if (!client.IsInstalled && client.Id != "BuiltIn" && client.Id != "Auto")
        {
            CustomVncPanel.Visibility = Visibility.Visible;
            CustomVncHeaderTextBlock.Text = $"{client.DisplayName} Executable Path";
            VncClientNotDetectedBannerText.Text = $"'{client.DisplayName}' was not detected in standard system locations. Please specify the executable path.";
            VncClientNotDetectedBanner.Visibility = Visibility.Visible;
        }
        else
        {
            CustomVncPanel.Visibility = Visibility.Collapsed;
            VncClientNotDetectedBanner.Visibility = Visibility.Collapsed;
        }
    }

    private void OnBrowseCustomVncClick(object sender, RoutedEventArgs e)
    {
        var client = VncClientComboBox.SelectedItem as VncClientInfo;
        var title = client != null && client.Id != "Custom"
            ? $"Locate {client.DisplayName} Executable"
            : "Select Custom VNC Client Executable";

        var dlg = new OpenFileDialog
        {
            Title = title,
            Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dlg.ShowDialog() == true)
        {
            CustomVncPathTextBox.Text = dlg.FileName;
            if (_viewModel != null)
            {
                _viewModel.Settings.CustomVncClientPath = dlg.FileName;
                _ = _viewModel.SaveSettingsAsync();
            }
        }
    }

    private void OnCustomVncPathChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoaded || _viewModel == null) return;
        _viewModel.Settings.CustomVncClientPath = CustomVncPathTextBox.Text.Trim();
        _ = _viewModel.SaveSettingsAsync();
    }

    private void OnCustomVncArgsChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoaded || _viewModel == null) return;
        _viewModel.Settings.CustomVncClientArgs = CustomVncArgsTextBox.Text.Trim();
        _ = _viewModel.SaveSettingsAsync();
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "RemoteManager");
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{dir}\"",
            UseShellExecute = true
        });
    }

    private void OnClearWebCacheClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var webProfilesDir = Path.Combine(appData, "RemoteManager", "WebProfiles");
            if (Directory.Exists(webProfilesDir))
            {
                Directory.Delete(webProfilesDir, recursive: true);
                MessageBox.Show("Web session cache and profiles cleared successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("No cached web profiles found.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not clear cache (some sessions may be open): {ex.Message}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnLoggingOptionChanged(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded || _viewModel == null) return;

        var isEnabled = LoggingEnabledSwitch.IsChecked == true;
        _viewModel.Settings.IsLoggingEnabled = isEnabled;
        _viewModel.Settings.LogAppLifecycle = LogAppSwitch.IsChecked == true;
        _viewModel.Settings.LogDatabase = LogDbSwitch.IsChecked == true;
        _viewModel.Settings.LogRdp = LogRdpSwitch.IsChecked == true;
        _viewModel.Settings.LogSsh = LogSshSwitch.IsChecked == true;
        _viewModel.Settings.LogVnc = LogVncSwitch.IsChecked == true;
        _viewModel.Settings.LogWeb = LogWebSwitch.IsChecked == true;
        _viewModel.Settings.LogSecurity = LogSecuritySwitch.IsChecked == true;

        ModuleOptionsPanel.IsEnabled = isEnabled;
        LogLevelContainerGrid.IsEnabled = isEnabled;

        _ = _viewModel.SaveSettingsAsync();
    }

    private void OnMinLogLevelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded || _viewModel == null) return;
        if (MinLogLevelComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            _viewModel.Settings.MinimumLogLevel = item.Tag.ToString() ?? "Debug";
            _ = _viewModel.SaveSettingsAsync();
        }
    }
}
