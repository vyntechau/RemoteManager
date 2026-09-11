using System.Diagnostics;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RemoteManager.Core.Interfaces;
using RemoteManager.Core.Logging;
using RemoteManager.Core.Models;
using ProtocolType = RemoteManager.Core.Models.ProtocolType;

namespace RemoteManager.App.Views;

public partial class ConnectionEditView : UserControl
{
    public ConnectionItem? Existing { get; private set; }
    public ConnectionItem? ResultConnection { get; private set; }
    public Credential? ResultCredential { get; private set; }

    public event Action<ConnectionItem, Credential?, bool>? ConnectionSaved;
    public event Action? CancelRequested;

    private ProtocolType _selectedProtocol = ProtocolType.RDP;
    private DisplayMode _selectedDisplayMode = DisplayMode.Tabbed;
    private IEncryptionService? _encryptionService;
    private bool _isInlinePasswordRevealed = false;
    private bool _isInitialized = false;

    public ConnectionEditView()
    {
        InitializeComponent();
        _isInitialized = true;
        UpdateProtocolUI();
        UpdateDisplayModeUI();
        UpdatePreview();
    }

    public void LoadConnection(ConnectionItem? existing, IEnumerable<Credential> credentials, IEncryptionService? encryptionService = null)
    {
        Existing = existing;
        ResultConnection = null;
        ResultCredential = null;
        _encryptionService = encryptionService;
        _isInlinePasswordRevealed = false;

        // Reset inline fields
        if (InlineUsernameInput != null) InlineUsernameInput.Text = string.Empty;
        if (InlinePasswordInput != null) InlinePasswordInput.Password = string.Empty;
        if (InlinePasswordVisibleInput != null)
        {
            InlinePasswordVisibleInput.Text = string.Empty;
            InlinePasswordVisibleInput.Visibility = Visibility.Collapsed;
        }
        if (InlinePasswordInput != null) InlinePasswordInput.Visibility = Visibility.Visible;
        if (InlinePasswordEyeIcon != null) InlinePasswordEyeIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Eye24;
        if (InlineDomainInput != null) InlineDomainInput.Text = string.Empty;
        if (InlineTitleInput != null) InlineTitleInput.Text = string.Empty;
        if (SaveToVaultCheckbox != null) SaveToVaultCheckbox.IsChecked = true;

        if (RadioVaultCred != null) RadioVaultCred.IsChecked = true;
        if (RadioCustomCred != null) RadioCustomCred.IsChecked = false;
        if (VaultPickerArea != null) VaultPickerArea.Visibility = Visibility.Visible;
        if (InlineCredArea != null) InlineCredArea.Visibility = Visibility.Collapsed;

        // Populate credential dropdown
        var credList = credentials.ToList();
        credList.Insert(0, new Credential { Id = Guid.Empty, Title = "(None - Prompt during login)" });
        if (CredentialPicker != null)
        {
            CredentialPicker.ItemsSource = credList;
            CredentialPicker.SelectedIndex = 0;
        }

        // Reset errors
        if (NameErrorText != null) NameErrorText.Visibility = Visibility.Collapsed;
        if (HostErrorText != null) HostErrorText.Visibility = Visibility.Collapsed;
        if (PortErrorText != null) PortErrorText.Visibility = Visibility.Collapsed;

        if (existing != null)
        {
            if (PageTitleText != null) PageTitleText.Text = $"Edit Connection: {existing.DisplayName}";
            if (PageSubtitleText != null) PageSubtitleText.Text = $"Modify endpoint, credentials, or session mode for '{existing.DisplayName}'";
            if (NameInput != null) NameInput.Text = existing.Name;
            if (HostInput != null) HostInput.Text = existing.Host;
            if (PortInput != null) PortInput.Text = existing.Port.ToString();
            _selectedProtocol = existing.Protocol;
            _selectedDisplayMode = existing.DisplayMode;

            if (existing.CredentialId.HasValue && CredentialPicker != null)
            {
                CredentialPicker.SelectedValue = existing.CredentialId.Value;

                // Pre-populate inline fields if the user switches to inline entry
                var matched = credList.FirstOrDefault(c => c.Id == existing.CredentialId.Value);
                if (matched != null)
                {
                    if (InlineUsernameInput != null) InlineUsernameInput.Text = matched.Username;
                    if (InlineDomainInput != null) InlineDomainInput.Text = matched.Domain ?? string.Empty;
                    if (InlineTitleInput != null) InlineTitleInput.Text = matched.Title;

                    if (!string.IsNullOrEmpty(matched.EncryptedPassword) && _encryptionService != null)
                    {
                        try
                        {
                            var dec = _encryptionService.Decrypt(matched.EncryptedPassword);
                            if (InlinePasswordInput != null) InlinePasswordInput.Password = dec;
                            if (InlinePasswordVisibleInput != null) InlinePasswordVisibleInput.Text = dec;
                        }
                        catch
                        {
                            // Ignore decryption failure
                        }
                    }
                }
            }
        }
        else
        {
            if (PageTitleText != null) PageTitleText.Text = "New Remote Connection";
            if (PageSubtitleText != null) PageSubtitleText.Text = "Configure server endpoints, protocol parameters, and authentication credentials";
            if (NameInput != null) NameInput.Text = string.Empty;
            if (HostInput != null) HostInput.Text = string.Empty;
            if (PortInput != null) PortInput.Text = "3389";
            _selectedProtocol = ProtocolType.RDP;
            _selectedDisplayMode = DisplayMode.Tabbed;
        }

        UpdateProtocolUI();
        UpdateDisplayModeUI();
        UpdatePreview();
    }

    private void UpdateProtocolUI()
    {
        if (!_isInitialized || TileRdp == null || TileSsh == null || TileVnc == null || TileWeb == null) return;

        ResetTileBorders();
        var accentBrush = TryFindResource("AccentFillColorDefaultBrush") as Brush ?? Brushes.DodgerBlue;
        var defaultBorderBrush = TryFindResource("ControlElevationBorderBrush") as Brush ?? Brushes.Gray;

        TileRdp.BorderBrush = _selectedProtocol == ProtocolType.RDP ? accentBrush : defaultBorderBrush;
        TileSsh.BorderBrush = _selectedProtocol == ProtocolType.SSH ? accentBrush : defaultBorderBrush;
        TileVnc.BorderBrush = _selectedProtocol == ProtocolType.VNC ? accentBrush : defaultBorderBrush;
        TileWeb.BorderBrush = _selectedProtocol == ProtocolType.Web ? accentBrush : defaultBorderBrush;

        TileRdp.BorderThickness = new Thickness(_selectedProtocol == ProtocolType.RDP ? 2 : 1);
        TileSsh.BorderThickness = new Thickness(_selectedProtocol == ProtocolType.SSH ? 2 : 1);
        TileVnc.BorderThickness = new Thickness(_selectedProtocol == ProtocolType.VNC ? 2 : 1);
        TileWeb.BorderThickness = new Thickness(_selectedProtocol == ProtocolType.Web ? 2 : 1);

        if (PreviewProtocolBadge != null)
        {
            PreviewProtocolBadge.Background = new SolidColorBrush(_selectedProtocol switch
            {
                ProtocolType.RDP => (Color)ColorConverter.ConvertFromString("#0078D4"),
                ProtocolType.SSH => (Color)ColorConverter.ConvertFromString("#107C41"),
                ProtocolType.VNC => (Color)ColorConverter.ConvertFromString("#D83B01"),
                ProtocolType.Web => (Color)ColorConverter.ConvertFromString("#881798"),
                _ => (Color)ColorConverter.ConvertFromString("#0078D4")
            });
        }
        if (PreviewProtocolText != null)
        {
            PreviewProtocolText.Text = _selectedProtocol.ToString();
        }
    }

    private void UpdateDisplayModeUI()
    {
        if (!_isInitialized || TileTabbed == null || TileFullscreen == null || TileExternal == null) return;

        var accentBrush = TryFindResource("AccentFillColorDefaultBrush") as Brush ?? Brushes.DodgerBlue;
        var defaultBorderBrush = TryFindResource("ControlElevationBorderBrush") as Brush ?? Brushes.Gray;

        TileTabbed.BorderBrush = _selectedDisplayMode == DisplayMode.Tabbed ? accentBrush : defaultBorderBrush;
        TileFullscreen.BorderBrush = _selectedDisplayMode == DisplayMode.Fullscreen ? accentBrush : defaultBorderBrush;
        TileExternal.BorderBrush = _selectedDisplayMode == DisplayMode.ExternalApp ? accentBrush : defaultBorderBrush;

        TileTabbed.BorderThickness = new Thickness(_selectedDisplayMode == DisplayMode.Tabbed ? 2 : 1);
        TileFullscreen.BorderThickness = new Thickness(_selectedDisplayMode == DisplayMode.Fullscreen ? 2 : 1);
        TileExternal.BorderThickness = new Thickness(_selectedDisplayMode == DisplayMode.ExternalApp ? 2 : 1);

        if (PreviewDisplayModeText != null)
        {
            PreviewDisplayModeText.Text = _selectedDisplayMode switch
            {
                DisplayMode.Tabbed => "Embedded Tab",
                DisplayMode.Fullscreen => "Fullscreen (F11)",
                DisplayMode.ExternalApp => "Native Window",
                _ => _selectedDisplayMode.ToString()
            };
        }
    }

    private void ResetTileBorders()
    {
        if (!_isInitialized || TileRdp == null || TileSsh == null || TileVnc == null || TileWeb == null) return;

        var defaultBorder = TryFindResource("ControlElevationBorderBrush") as Brush ?? Brushes.Gray;
        TileRdp.BorderBrush = defaultBorder;
        TileSsh.BorderBrush = defaultBorder;
        TileVnc.BorderBrush = defaultBorder;
        TileWeb.BorderBrush = defaultBorder;
    }

    private void OnSelectRdp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _selectedProtocol = ProtocolType.RDP;
        if (PortInput != null && (string.IsNullOrWhiteSpace(PortInput.Text) || PortInput.Text is "22" or "5900" or "443"))
            PortInput.Text = "3389";
        UpdateProtocolUI();
        UpdatePreview();
    }

    private void OnSelectSsh(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _selectedProtocol = ProtocolType.SSH;
        if (PortInput != null && (string.IsNullOrWhiteSpace(PortInput.Text) || PortInput.Text is "3389" or "5900" or "443"))
            PortInput.Text = "22";
        UpdateProtocolUI();
        UpdatePreview();
    }

    private void OnSelectVnc(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _selectedProtocol = ProtocolType.VNC;
        if (PortInput != null && (string.IsNullOrWhiteSpace(PortInput.Text) || PortInput.Text is "3389" or "22" or "443"))
            PortInput.Text = "5900";
        UpdateProtocolUI();
        UpdatePreview();
    }

    private void OnSelectWeb(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _selectedProtocol = ProtocolType.Web;
        if (PortInput != null && (string.IsNullOrWhiteSpace(PortInput.Text) || PortInput.Text is "3389" or "22" or "5900"))
            PortInput.Text = "443";
        UpdateProtocolUI();
        UpdatePreview();
    }

    private void OnSelectTabbed(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _selectedDisplayMode = DisplayMode.Tabbed;
        UpdateDisplayModeUI();
    }

    private void OnSelectFullscreen(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _selectedDisplayMode = DisplayMode.Fullscreen;
        UpdateDisplayModeUI();
    }

    private void OnSelectExternal(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _selectedDisplayMode = DisplayMode.ExternalApp;
        UpdateDisplayModeUI();
    }

    private void OnFormInputChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized) return;

        if (NameInput != null && !string.IsNullOrWhiteSpace(NameInput.Text))
        {
            if (NameErrorText != null) NameErrorText.Visibility = Visibility.Collapsed;
        }
        if (HostInput != null && !string.IsNullOrWhiteSpace(HostInput.Text))
        {
            if (HostErrorText != null) HostErrorText.Visibility = Visibility.Collapsed;
        }
        if (PortInput != null && int.TryParse(PortInput.Text, out var p) && p is > 0 and <= 65535)
        {
            if (PortErrorText != null) PortErrorText.Visibility = Visibility.Collapsed;
        }

        UpdatePreview();
    }

    private void OnCredModeChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || VaultPickerArea == null || InlineCredArea == null) return;
        bool isVault = RadioVaultCred?.IsChecked == true;
        VaultPickerArea.Visibility = isVault ? Visibility.Visible : Visibility.Collapsed;
        InlineCredArea.Visibility = isVault ? Visibility.Collapsed : Visibility.Visible;
        UpdatePreview();
    }

    private void OnCredentialSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        UpdatePreview();
    }

    private void OnInlineCredChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized) return;
        UpdatePreview();
    }

    private void OnInlinePasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        if (!_isInlinePasswordRevealed && InlinePasswordVisibleInput != null && InlinePasswordInput != null)
        {
            InlinePasswordVisibleInput.Text = InlinePasswordInput.Password;
        }
    }

    private void OnInlineVisiblePasswordChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized) return;
        if (_isInlinePasswordRevealed && InlinePasswordInput != null && InlinePasswordVisibleInput != null)
        {
            InlinePasswordInput.Password = InlinePasswordVisibleInput.Text;
        }
    }

    private void OnToggleInlinePasswordVisibility(object sender, RoutedEventArgs e)
    {
        _isInlinePasswordRevealed = !_isInlinePasswordRevealed;
        if (_isInlinePasswordRevealed)
        {
            if (InlinePasswordVisibleInput != null && InlinePasswordInput != null)
            {
                InlinePasswordVisibleInput.Text = InlinePasswordInput.Password;
                InlinePasswordInput.Visibility = Visibility.Collapsed;
                InlinePasswordVisibleInput.Visibility = Visibility.Visible;
                InlinePasswordVisibleInput.Focus();
            }
            if (InlinePasswordEyeIcon != null) InlinePasswordEyeIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.EyeOff24;
        }
        else
        {
            if (InlinePasswordVisibleInput != null && InlinePasswordInput != null)
            {
                InlinePasswordInput.Password = InlinePasswordVisibleInput.Text;
                InlinePasswordVisibleInput.Visibility = Visibility.Collapsed;
                InlinePasswordInput.Visibility = Visibility.Visible;
                InlinePasswordInput.Focus();
            }
            if (InlinePasswordEyeIcon != null) InlinePasswordEyeIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Eye24;
        }
    }

    private void OnSaveToVaultChecked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || VaultTitlePanel == null) return;
        VaultTitlePanel.Visibility = SaveToVaultCheckbox?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdatePreview()
    {
        if (!_isInitialized || PreviewNameText == null || PreviewAddressText == null) return;

        var name = string.IsNullOrWhiteSpace(NameInput?.Text) ? "New Remote Connection" : NameInput.Text.Trim();
        var host = string.IsNullOrWhiteSpace(HostInput?.Text) ? "host" : HostInput.Text.Trim();
        var port = PortInput?.Text?.Trim() ?? "3389";

        PreviewNameText.Text = name;
        PreviewAddressText.Text = $"{host}:{port}";

        if (PreviewCredentialText != null)
        {
            if (RadioCustomCred?.IsChecked == true)
            {
                if (!string.IsNullOrWhiteSpace(InlineUsernameInput?.Text))
                {
                    PreviewCredentialText.Text = $"👤 {InlineUsernameInput.Text.Trim()}";
                }
                else
                {
                    PreviewCredentialText.Text = "(Custom / Inline)";
                }
            }
            else
            {
                if (CredentialPicker?.SelectedItem is Credential cred && cred.Id != Guid.Empty)
                {
                    PreviewCredentialText.Text = cred.Title;
                }
                else
                {
                    PreviewCredentialText.Text = "(None / Prompt)";
                }
            }
        }
    }

    private async void OnTestConnectivityClick(object sender, RoutedEventArgs e)
    {
        var host = HostInput?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            if (HostErrorText != null) HostErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (!int.TryParse(PortInput?.Text, out var port) || port is <= 0 or > 65535)
        {
            if (PortErrorText != null) PortErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (TestEndpointButton != null) TestEndpointButton.IsEnabled = false;
        if (TestEndpointButtonText != null) TestEndpointButtonText.Text = "Testing...";
        if (PreviewPingText != null) PreviewPingText.Text = "Checking...";

        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(2500));
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(2500, cts.Token));

            if (completed == connectTask && client.Connected)
            {
                sw.Stop();
                var ms = Math.Max(1, (int)sw.ElapsedMilliseconds);
                if (PreviewPingText != null) PreviewPingText.Text = $"Online ({ms} ms)";
                LogEngine.Instance.Info("Network", $"Test connectivity to {host}:{port} succeeded in {ms}ms");
            }
            else
            {
                if (PreviewPingText != null) PreviewPingText.Text = "Unreachable";
            }
        }
        catch
        {
            if (PreviewPingText != null) PreviewPingText.Text = "Unreachable";
        }
        finally
        {
            if (TestEndpointButton != null) TestEndpointButton.IsEnabled = true;
            if (TestEndpointButtonText != null) TestEndpointButtonText.Text = "Test Port Reachability";
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (ValidateAndBuild(out var item, out var inlineCred))
        {
            ConnectionSaved?.Invoke(item, inlineCred, false);
        }
    }

    private void OnSaveAndConnectClick(object sender, RoutedEventArgs e)
    {
        if (ValidateAndBuild(out var item, out var inlineCred))
        {
            ConnectionSaved?.Invoke(item, inlineCred, true);
        }
    }

    private bool ValidateAndBuild(out ConnectionItem connection, out Credential? inlineCred)
    {
        connection = new ConnectionItem();
        inlineCred = null;
        var isValid = true;

        if (NameInput == null || string.IsNullOrWhiteSpace(NameInput.Text))
        {
            if (NameErrorText != null) NameErrorText.Visibility = Visibility.Visible;
            isValid = false;
        }

        if (HostInput == null || string.IsNullOrWhiteSpace(HostInput.Text))
        {
            if (HostErrorText != null) HostErrorText.Visibility = Visibility.Visible;
            isValid = false;
        }

        int port = 3389;
        if (PortInput == null || !int.TryParse(PortInput.Text, out port) || port is <= 0 or > 65535)
        {
            if (PortErrorText != null) PortErrorText.Visibility = Visibility.Visible;
            isValid = false;
        }

        if (!isValid) return false;

        Guid? selectedCredId = null;

        if (RadioCustomCred?.IsChecked == true)
        {
            var rawUsername = InlineUsernameInput?.Text?.Trim() ?? string.Empty;
            var rawPassword = _isInlinePasswordRevealed ? InlinePasswordVisibleInput?.Text ?? string.Empty : InlinePasswordInput?.Password ?? string.Empty;
            var rawDomain = InlineDomainInput?.Text?.Trim();

            // If user entered username or password, create/update inline credential
            if (!string.IsNullOrWhiteSpace(rawUsername) || !string.IsNullOrWhiteSpace(rawPassword))
            {
                var title = string.IsNullOrWhiteSpace(InlineTitleInput?.Text)
                    ? $"{NameInput!.Text.Trim()} Credentials"
                    : InlineTitleInput.Text.Trim();

                var encryptedPassword = string.Empty;
                if (!string.IsNullOrEmpty(rawPassword) && _encryptionService != null)
                {
                    try
                    {
                        encryptedPassword = _encryptionService.Encrypt(rawPassword);
                    }
                    catch (Exception ex)
                    {
                        LogEngine.Instance.Error("Security", "Failed to encrypt inline password", ex);
                    }
                }

                inlineCred = new Credential
                {
                    Id = Existing?.CredentialId ?? Guid.NewGuid(),
                    Title = title,
                    Username = rawUsername,
                    Domain = string.IsNullOrWhiteSpace(rawDomain) ? null : rawDomain,
                    EncryptedPassword = encryptedPassword,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                selectedCredId = inlineCred.Id;
            }
        }
        else
        {
            if (CredentialPicker?.SelectedValue is Guid id && id != Guid.Empty)
            {
                selectedCredId = id;
            }
        }

        connection = new ConnectionItem
        {
            Id = Existing?.Id ?? Guid.NewGuid(),
            Name = NameInput!.Text.Trim(),
            Host = HostInput!.Text.Trim(),
            Port = port,
            Protocol = _selectedProtocol,
            DisplayMode = _selectedDisplayMode,
            CredentialId = selectedCredId,
            CreatedAt = Existing?.CreatedAt ?? DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        ResultConnection = connection;
        ResultCredential = inlineCred;

        return true;
    }
}
