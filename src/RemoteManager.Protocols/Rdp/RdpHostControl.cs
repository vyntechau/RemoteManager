using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using RemoteManager.Core.Logging;
using MediaColor = System.Windows.Media.Color;
using WpfButton = System.Windows.Controls.Button;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace RemoteManager.Protocols.Rdp;

public class RdpHostControl : ContentControl, IRdpHostControl
{
    private readonly WindowsFormsHost _host;
    private readonly RdpAxClient _rdpClient;
    private readonly TextBlock _statusTextBlock;
    private readonly Border _toolBar;

    public event Action? DisconnectRequested;

    public bool IsToolbarVisible
    {
        get => _toolBar.Visibility == Visibility.Visible;
        set => _toolBar.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

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
        Grid.SetRow(_host, 1);
        mainGrid.Children.Add(_host);

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
        _statusTextBlock.Text = $"Connecting to {server}:{port}...";
        LogEngine.Instance.Info("Protocol.RDP", $"Embedded RDP connecting to {server}:{port} (User: '{username}', Domain: '{domain}', Resolution: {width}x{height})");
        try
        {
            _rdpClient.ConnectServer(server, port, username, domain, password, width, height);
            _statusTextBlock.Text = $"Connected to {server}:{port}";
        }
        catch (Exception ex)
        {
            _statusTextBlock.Text = $"Error: {ex.Message}";
            LogEngine.Instance.Error("Protocol.RDP", $"Failed to connect embedded RDP to {server}:{port}", ex);
            throw;
        }
    }

    public void Disconnect()
    {
        LogEngine.Instance.Info("Protocol.RDP", "Embedded RDP disconnecting...");
        try
        {
            _rdpClient.DisconnectServer();
            _statusTextBlock.Text = "Disconnected";
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Warn("Protocol.RDP", "Exception during embedded RDP disconnect", ex);
        }
    }

    public void SendCtrlAltDel()
    {
        try
        {
            LogEngine.Instance.Info("Protocol.RDP", "Sending Ctrl+Alt+Del (via Ctrl+Alt+End) to remote RDP machine.");
            _host.Focus();
            if (_rdpClient.IsHandleCreated)
            {
                SetFocus(_rdpClient.Handle);
            }
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
            _host.Focus();
            if (_rdpClient.IsHandleCreated)
            {
                SetFocus(_rdpClient.Handle);
            }
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
            _host.Focus();
            if (_rdpClient.IsHandleCreated)
            {
                SetFocus(_rdpClient.Handle);
            }
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
