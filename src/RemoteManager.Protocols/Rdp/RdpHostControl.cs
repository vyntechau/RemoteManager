using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using RemoteManager.Core.Logging;
using MediaColor = System.Windows.Media.Color;
using MediaBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace RemoteManager.Protocols.Rdp;

public class RdpHostControl : ContentControl, IRdpHostControl
{
    private readonly WindowsFormsHost _host;
    private readonly RdpAxClient _rdpClient;
    private readonly TextBlock _statusTextBlock;
    private readonly Border _toolBar;

    // Disconnect overlay controls (prevents white blank screen on disconnect)
    private readonly Grid _disconnectOverlay;
    private readonly TextBlock _overlayTitle;
    private readonly TextBlock _overlayMessage;
    private readonly TextBlock _overlayDetails;

    // Cache connection parameters for easy one-click reconnect
    private string? _lastServer;
    private int _lastPort = 3389;
    private string? _lastUsername;
    private string? _lastDomain;
    private string? _lastPassword;
    private int _lastWidth = 1920;
    private int _lastHeight = 1080;

    public event Action? DisconnectRequested;
    public event Action<string, int, int>? Disconnected;
    public event Action? Connected;

    public bool IsToolbarVisible
    {
        get => _toolBar.Visibility == Visibility.Visible;
        set => _toolBar.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    private const uint GW_CHILD = 5;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_MENU = 0x12; // Alt
    private const byte VK_END = 0x23;
    private const byte VK_C = 0x43;
    private const byte VK_V = 0x56;

    public RdpHostControl()
    {
        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Top Toolbar
        _toolBar = new Border
        {
            Background = new SolidColorBrush(MediaColor.FromArgb(240, 30, 30, 30)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(50, 255, 255, 255)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 4, 10, 4)
        };

        var toolBarGrid = new Grid();
        toolBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _statusTextBlock = new TextBlock
        {
            Text = "RDP Session",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Foreground = new SolidColorBrush(MediaColor.FromArgb(200, 255, 255, 255))
        };
        Grid.SetColumn(_statusTextBlock, 0);
        toolBarGrid.Children.Add(_statusTextBlock);

        var actionsPanel = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Ctrl+Alt+Del
        var cadButton = new WpfButton
        {
            Content = "Ctrl+Alt+Del",
            Height = 26,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 11,
            ToolTip = "Send Ctrl+Alt+Del to remote machine"
        };
        cadButton.Click += (s, e) => SendCtrlAltDel();
        actionsPanel.Children.Add(cadButton);

        // Copy (Ctrl+C)
        var copyButton = new WpfButton
        {
            Content = "Copy",
            Height = 26,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 11,
            ToolTip = "Send Copy (Ctrl+C) to remote machine"
        };
        copyButton.Click += (s, e) => SendCopy();
        actionsPanel.Children.Add(copyButton);

        // Paste (Ctrl+V)
        var pasteButton = new WpfButton
        {
            Content = "Paste",
            Height = 26,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 11,
            ToolTip = "Send Paste (Ctrl+V) to remote machine"
        };
        pasteButton.Click += (s, e) => SendPaste();
        actionsPanel.Children.Add(pasteButton);

        // Disconnect
        var disconnectButton = new WpfButton
        {
            Content = "Disconnect",
            Height = 26,
            Padding = new Thickness(8, 0, 8, 0),
            FontSize = 11,
            ToolTip = "Disconnect from remote RDP server"
        };
        disconnectButton.Click += (s, e) =>
        {
            if (DisconnectRequested != null)
            {
                DisconnectRequested.Invoke();
            }
            else
            {
                Disconnect();
            }
        };
        actionsPanel.Children.Add(disconnectButton);

        Grid.SetColumn(actionsPanel, 1);
        toolBarGrid.Children.Add(actionsPanel);
        _toolBar.Child = toolBarGrid;

        Grid.SetRow(_toolBar, 0);
        mainGrid.Children.Add(_toolBar);

        // Host RDP ActiveX Control
        _host = new WindowsFormsHost();
        _rdpClient = new RdpAxClient();
        _host.Child = _rdpClient;

        // Auto-transfer Win32 focus to the native RDP ActiveX control whenever the control is clicked or focused
        _host.GotFocus += (s, e) => FocusRdp();
        _host.MouseDown += (s, e) => FocusRdp();
        _host.PreviewMouseDown += (s, e) => FocusRdp();
        _rdpClient.GotFocus += (s, e) => FocusRdp();
        _rdpClient.Click += (s, e) => FocusRdp();
        _rdpClient.HandleCreated += (s, e) => FocusRdp();
        Loaded += (s, e) => FocusRdp();
        GotFocus += (s, e) => FocusRdp();
        PreviewMouseDown += (s, e) =>
        {
            if (!_toolBar.IsMouseOver)
            {
                FocusRdp();
            }
        };

        // Enable file drag-and-drop: dragging files onto the RDP session places them onto the clipboard
        // and focuses the RDP session so they can be pasted immediately via Ctrl+V or right-click Paste.
        AllowDrop = true;
        PreviewDragOver += (s, e) =>
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                e.Effects = System.Windows.DragDropEffects.Copy;
                e.Handled = true;
            }
        };
        PreviewDrop += (s, e) =>
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    var fileDropList = new System.Collections.Specialized.StringCollection();
                    fileDropList.AddRange(files);
                    System.Windows.Clipboard.SetFileDropList(fileDropList);
                    LogEngine.Instance.Info("Protocol.RDP", $"Files placed on clipboard via drag-and-drop ({files.Length} items). Ready to paste into remote session.");
                    FocusRdp();
                    e.Handled = true;
                }
            }
        };

        _rdpClient.AllowDrop = true;
        _rdpClient.DragEnter += (s, e) =>
        {
            if (e.Data != null && e.Data.GetDataPresent(System.Windows.Forms.DataFormats.FileDrop))
            {
                e.Effect = System.Windows.Forms.DragDropEffects.Copy;
            }
        };
        _rdpClient.DragDrop += (s, e) =>
        {
            if (e.Data != null && e.Data.GetDataPresent(System.Windows.Forms.DataFormats.FileDrop))
            {
                if (e.Data.GetData(System.Windows.Forms.DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    var fileDropList = new System.Collections.Specialized.StringCollection();
                    fileDropList.AddRange(files);
                    System.Windows.Clipboard.SetFileDropList(fileDropList);
                    LogEngine.Instance.Info("Protocol.RDP", $"Files placed on clipboard via WinForms drag-drop ({files.Length} items). Ready to paste into remote session.");
                    FocusRdp();
                }
            }
        };

        Grid.SetRow(_host, 1);
        mainGrid.Children.Add(_host);

        // Disconnect Overlay: Shown in place of native Win32 window to avoid blank/white screen
        _disconnectOverlay = new Grid
        {
            Background = new SolidColorBrush(MediaColor.FromRgb(24, 24, 27)),
            Visibility = Visibility.Collapsed
        };

        var overlayCard = new Border
        {
            Background = new SolidColorBrush(MediaColor.FromRgb(39, 39, 42)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(32, 28, 32, 28),
            MaxWidth = 560,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = WpfVerticalAlignment.Center
        };

        var cardStack = new StackPanel();

        // Icon + Title header
        var headerPanel = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 16)
        };

        var iconBorder = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(22),
            Background = new SolidColorBrush(MediaColor.FromArgb(50, 245, 158, 11)), // Amber accent
            Margin = new Thickness(0, 0, 16, 0)
        };
        var iconText = new TextBlock
        {
            Text = "⚠️",
            FontSize = 20,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = WpfVerticalAlignment.Center
        };
        iconBorder.Child = iconText;
        headerPanel.Children.Add(iconBorder);

        var titleStack = new StackPanel { VerticalAlignment = WpfVerticalAlignment.Center };
        _overlayTitle = new TextBlock
        {
            Text = "Session Disconnected",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = MediaBrushes.White
        };
        titleStack.Children.Add(_overlayTitle);

        var statusSub = new TextBlock
        {
            Text = "Remote Desktop Connection Notice",
            FontSize = 12,
            Foreground = new SolidColorBrush(MediaColor.FromArgb(160, 255, 255, 255)),
            Margin = new Thickness(0, 2, 0, 0)
        };
        titleStack.Children.Add(statusSub);
        headerPanel.Children.Add(titleStack);

        cardStack.Children.Add(headerPanel);

        // Notice message box
        var messageBox = new Border
        {
            Background = new SolidColorBrush(MediaColor.FromRgb(24, 24, 27)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 16)
        };

        _overlayMessage = new TextBlock
        {
            Text = "The remote desktop session has ended.",
            FontSize = 13,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(228, 228, 231)),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20
        };
        messageBox.Child = _overlayMessage;
        cardStack.Children.Add(messageBox);

        // Technical details note
        _overlayDetails = new TextBlock
        {
            Text = string.Empty,
            FontSize = 11,
            Foreground = new SolidColorBrush(MediaColor.FromArgb(140, 255, 255, 255)),
            Margin = new Thickness(0, 0, 0, 20)
        };
        cardStack.Children.Add(_overlayDetails);

        // Actions: Reconnect & Close Tab
        var buttonPanel = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right
        };

        var reconnectBtn = new WpfButton
        {
            Content = "Reconnect",
            Height = 32,
            Padding = new Thickness(16, 0, 16, 0),
            Background = new SolidColorBrush(MediaColor.FromRgb(0, 120, 212)),
            Foreground = MediaBrushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(0, 0, 10, 0)
        };
        reconnectBtn.Click += (s, e) => Reconnect();
        buttonPanel.Children.Add(reconnectBtn);

        var closeTabBtn = new WpfButton
        {
            Content = "Close Tab",
            Height = 32,
            Padding = new Thickness(16, 0, 16, 0),
            Background = new SolidColorBrush(MediaColor.FromRgb(63, 63, 70)),
            Foreground = MediaBrushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 12,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        closeTabBtn.Click += (s, e) =>
        {
            if (DisconnectRequested != null)
                DisconnectRequested.Invoke();
            else
                Disconnect();
        };
        buttonPanel.Children.Add(closeTabBtn);

        cardStack.Children.Add(buttonPanel);
        overlayCard.Child = cardStack;
        _disconnectOverlay.Children.Add(overlayCard);

        Grid.SetRow(_disconnectOverlay, 1);
        mainGrid.Children.Add(_disconnectOverlay);

        // Subscribe to ActiveX client events
        _rdpClient.Connected += () =>
        {
            Dispatcher.Invoke(() =>
            {
                _disconnectOverlay.Visibility = Visibility.Collapsed;
                _host.Visibility = Visibility.Visible;
                _statusTextBlock.Text = $"Connected to {_lastServer}:{_lastPort}";
                _statusTextBlock.Foreground = new SolidColorBrush(MediaColor.FromArgb(200, 255, 255, 255));
                FocusRdp();
                Connected?.Invoke();
            });
        };

        _rdpClient.Disconnected += (discReason, extReason, description) =>
        {
            Dispatcher.Invoke(() =>
            {
                HandleDisconnected(discReason, extReason, description);
            });
        };

        Content = mainGrid;
    }

    public void Connect(
        string server,
        int port,
        string? username,
        string? domain,
        string? password,
        int width = 1920,
        int height = 1080)
    {
        _lastServer = server;
        _lastPort = port;
        _lastUsername = username;
        _lastDomain = domain;
        _lastPassword = password;
        _lastWidth = width;
        _lastHeight = height;

        _disconnectOverlay.Visibility = Visibility.Collapsed;
        _host.Visibility = Visibility.Visible;
        _statusTextBlock.Text = $"Connecting to {server}:{port}...";
        _statusTextBlock.Foreground = new SolidColorBrush(MediaColor.FromArgb(200, 255, 255, 255));

        LogEngine.Instance.Info("Protocol.RDP", $"Embedded RDP connecting to {server}:{port} (User: '{username}', Domain: '{domain}', Resolution: {width}x{height})");
        try
        {
            _rdpClient.ConnectServer(server, port, username, domain, password, width, height);
            _statusTextBlock.Text = $"Connected to {server}:{port}";
        }
        catch (Exception ex)
        {
            _statusTextBlock.Text = $"Error: {ex.Message}";
            _statusTextBlock.Foreground = new SolidColorBrush(MediaColor.FromRgb(239, 68, 68));
            LogEngine.Instance.Error("Protocol.RDP", $"Failed to connect embedded RDP to {server}:{port}", ex);
            HandleDisconnected(0, 0, $"Failed to connect to {server}:{port}: {ex.Message}");
            throw;
        }
    }

    public void Reconnect()
    {
        if (string.IsNullOrEmpty(_lastServer)) return;
        LogEngine.Instance.Info("Protocol.RDP", $"Reconnecting embedded RDP to {_lastServer}:{_lastPort}...");
        _disconnectOverlay.Visibility = Visibility.Collapsed;
        _host.Visibility = Visibility.Visible;
        Connect(_lastServer, _lastPort, _lastUsername, _lastDomain, _lastPassword, _lastWidth, _lastHeight);
    }

    public void Disconnect()
    {
        LogEngine.Instance.Info("Protocol.RDP", "Embedded RDP disconnecting...");
        try
        {
            _rdpClient.DisconnectServer();
            _statusTextBlock.Text = "Disconnected";
            _statusTextBlock.Foreground = new SolidColorBrush(MediaColor.FromArgb(180, 255, 255, 255));
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Warn("Protocol.RDP", "Exception during embedded RDP disconnect", ex);
        }
    }

    private void HandleDisconnected(int discReason, int extReason, string description)
    {
        // 1. Hide native Win32 host so the blank white window does not cover the WPF UI
        _host.Visibility = Visibility.Collapsed;

        // 2. Format title and messages based on reason
        bool isAnotherUser = extReason == 5;
        if (isAnotherUser)
        {
            _overlayTitle.Text = "Another User Connected";
            _statusTextBlock.Text = "Disconnected: Another user connected to the remote computer";
        }
        else
        {
            _overlayTitle.Text = "Session Disconnected";
            _statusTextBlock.Text = $"Disconnected ({discReason}): {description}";
        }

        _statusTextBlock.Foreground = new SolidColorBrush(MediaColor.FromRgb(245, 158, 11)); // Amber
        _overlayMessage.Text = description;
        _overlayDetails.Text = $"Host: {_lastServer}:{_lastPort} • Disconnect Code: {discReason} (Extended: {extReason})";

        // 3. Show modern overlay
        _disconnectOverlay.Visibility = Visibility.Visible;

        // 4. Notify listeners
        Disconnected?.Invoke(description, discReason, extReason);
    }

    public void FocusRdp()
    {
        try
        {
            _host.Focus();
            if (_rdpClient.IsHandleCreated)
            {
                _rdpClient.Focus();
                IntPtr target = _rdpClient.Handle;
                IntPtr child = GetWindow(target, GW_CHILD);
                if (child != IntPtr.Zero)
                {
                    IntPtr grandChild = GetWindow(child, GW_CHILD);
                    target = grandChild != IntPtr.Zero ? grandChild : child;
                }
                SetFocus(target);
            }
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Debug("Protocol.RDP", $"FocusRdp error: {ex.Message}");
        }
    }

    public void SendCtrlAltDel()
    {
        try
        {
            LogEngine.Instance.Info("Protocol.RDP", "Sending Ctrl+Alt+Del (via Ctrl+Alt+End) to remote RDP machine.");
            FocusRdp();
            Thread.Sleep(50);
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
            keybd_event(VK_END, 0, 0, UIntPtr.Zero);
            keybd_event(VK_END, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("Protocol.RDP", "Failed to send Ctrl+Alt+Del", ex);
        }
    }

    public void SendCopy()
    {
        try
        {
            LogEngine.Instance.Info("Protocol.RDP", "Sending Copy (Ctrl+C) to remote RDP machine.");
            FocusRdp();
            Thread.Sleep(50);
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_C, 0, 0, UIntPtr.Zero);
            keybd_event(VK_C, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("Protocol.RDP", "Failed to send Copy", ex);
        }
    }

    public void SendPaste()
    {
        try
        {
            LogEngine.Instance.Info("Protocol.RDP", "Sending Paste (Ctrl+V) to remote RDP machine.");
            FocusRdp();
            Thread.Sleep(50);
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("Protocol.RDP", "Failed to send Paste", ex);
        }
    }

    public bool IsConnected => _rdpClient.IsConnected;
}
