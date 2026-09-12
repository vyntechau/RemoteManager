using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using VncSharpCore;
using MediaColor = System.Windows.Media.Color;
using WpfButton = System.Windows.Controls.Button;
using WpfOrientation = System.Windows.Controls.Orientation;
using RemoteManager.Core.Logging;

namespace RemoteManager.Protocols.Vnc;

public class VncHostControl : ContentControl, IVncHostControl, IDisposable
{
    private readonly WindowsFormsHost _host;
    private readonly RemoteDesktop _remoteDesktop;
    private readonly TextBlock _statusTextBlock;
    private readonly WpfButton _scaleButton;
    private readonly Border _toolBar;
    private bool _scaled = true;
    private bool _isDisposed;

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<string>? Error;

    public bool IsConnected => _remoteDesktop.IsConnected;

    public bool IsToolbarVisible
    {
        get => _toolBar.Visibility == Visibility.Visible;
        set => _toolBar.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public VncHostControl()
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
            Text = "VNC Session",
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

        var cadButton = new WpfButton
        {
            Content = "Ctrl+Alt+Del",
            Height = 26,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 11,
            ToolTip = "Send Ctrl+Alt+Del keystroke sequence to remote machine"
        };
        cadButton.Click += (s, e) => SendCtrlAltDel();
        actionsPanel.Children.Add(cadButton);

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

        _scaleButton = new WpfButton
        {
            Content = "Scale: On",
            Height = 26,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 11,
            ToolTip = "Toggle remote screen scaling"
        };
        _scaleButton.Click += (s, e) => ToggleScale();
        actionsPanel.Children.Add(_scaleButton);

        var disconnectButton = new WpfButton
        {
            Content = "Disconnect",
            Height = 26,
            Padding = new Thickness(8, 0, 8, 0),
            FontSize = 11,
            ToolTip = "Disconnect from remote VNC server"
        };
        disconnectButton.Click += (s, e) => Disconnect();
        actionsPanel.Children.Add(disconnectButton);

        Grid.SetColumn(actionsPanel, 1);
        toolBarGrid.Children.Add(actionsPanel);
        _toolBar.Child = toolBarGrid;

        Grid.SetRow(_toolBar, 0);
        mainGrid.Children.Add(_toolBar);

        // Host VNC RemoteDesktop Control
        _host = new WindowsFormsHost();
        _remoteDesktop = new RemoteDesktop
        {
            AutoScroll = true,
            Scaled = true
        };

        _remoteDesktop.ConnectComplete += OnConnectComplete;
        _remoteDesktop.ConnectionLost += OnConnectionLost;

        _host.Child = _remoteDesktop;
        Grid.SetRow(_host, 1);
        mainGrid.Children.Add(_host);

        Content = mainGrid;

        Loaded += (s, e) =>
        {
            RefreshDesktop();
        };
    }

    public void Connect(string host, int port = 5900, string? password = null)
    {
        if (string.IsNullOrWhiteSpace(host)) return;

        host = host.Trim();
        if (host.Contains(':'))
        {
            var parts = host.Split(':');
            host = parts[0];
            if (int.TryParse(parts[1], out var parsedPort) && parsedPort > 0)
            {
                port = parsedPort;
            }
        }

        var vncPort = port > 0 ? port : 5900;
        if (vncPort < 1 || vncPort > 65535)
        {
            vncPort = 5900;
        }

        try
        {
            if (_remoteDesktop.IsConnected)
            {
                _remoteDesktop.Disconnect();
            }

            _statusTextBlock.Text = $"Connecting to {host}:{vncPort}...";
            LogEngine.Instance.Info("Protocol.VNC", $"Initiating VNC connection to {host}:{vncPort} (Scaled: {_scaled})");

            _remoteDesktop.GetPassword = new AuthenticateDelegate(() =>
            {
                // If a password was pre-configured (from saved credentials), use it directly
                if (!string.IsNullOrEmpty(password))
                {
                    LogEngine.Instance.Debug("Protocol.VNC", "Using pre-configured password for authentication.");
                    return password;
                }

                // Server is asking for a password but none was stored — prompt the user
                LogEngine.Instance.Info("Protocol.VNC", $"VNC server {host}:{vncPort} requested authentication password. Prompting user dialog...");
                string? enteredPassword = null;
                Dispatcher.Invoke(() =>
                {
                    var serverInfo = $"{host}:{vncPort}";
                    var dialog = new VncPasswordDialog(serverInfo);
                    var parentWindow = Window.GetWindow(this)
                        ?? System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                        ?? System.Windows.Application.Current?.MainWindow;
                    if (parentWindow != null)
                    {
                        dialog.Owner = parentWindow;
                    }
                    dialog.CenterOnElement(this);
                    if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.EnteredPassword))
                    {
                        enteredPassword = dialog.EnteredPassword;
                    }
                });

                if (enteredPassword == null)
                {
                    LogEngine.Instance.Warn("Protocol.VNC", "User cancelled VNC password prompt.");
                    Dispatcher.Invoke(() =>
                    {
                        _statusTextBlock.Text = "Authentication cancelled";
                    });
                }
                else
                {
                    LogEngine.Instance.Info("Protocol.VNC", "VNC password entered by user.");
                }
                return enteredPassword!;
            });
            _remoteDesktop.VncPort = vncPort;

            // Connect using host, viewOnly=false, scaled.
            // This overload sets display=0 and connects to VncPort directly,
            // avoiding port out of range or wrong port offset.
            _remoteDesktop.Connect(host, false, _scaled);
        }
        catch (Exception ex)
        {
            var fullMsg = ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;
            LogEngine.Instance.Error("Protocol.VNC", $"VNC connection error to {host}:{vncPort}: {fullMsg}", ex);
            System.Diagnostics.Debug.WriteLine($"[VNC Connect Exception] {ex}\nStackTrace: {ex.StackTrace}");
            _statusTextBlock.Text = $"Error: {fullMsg}";
            Error?.Invoke(fullMsg);
        }
    }

    public void Disconnect()
    {
        LogEngine.Instance.Info("Protocol.VNC", "Disconnecting VNC session...");
        try
        {
            if (_remoteDesktop.IsConnected)
            {
                _remoteDesktop.Disconnect();
            }
        }
        catch { }

        _statusTextBlock.Text = "Disconnected";
        Disconnected?.Invoke();
    }

    public void SendCtrlAltDel()
    {
        if (_remoteDesktop.IsConnected)
        {
            try
            {
                LogEngine.Instance.Info("Protocol.VNC", "Sending Ctrl+Alt+Del special key sequence.");
                _remoteDesktop.SendSpecialKeys(SpecialKeys.CtrlAltDel);
            }
            catch (Exception ex)
            {
                LogEngine.Instance.Error("Protocol.VNC", "Failed to send Ctrl+Alt+Del", ex);
                Error?.Invoke($"Failed to send Ctrl+Alt+Del: {ex.Message}");
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_C = 0x43;
    private const byte VK_V = 0x56;

    public void SendCopy()
    {
        try
        {
            LogEngine.Instance.Info("Protocol.VNC", "Sending Copy (Ctrl+C) to remote VNC session.");
            _host.Focus();
            if (_remoteDesktop.IsHandleCreated)
            {
                SetFocus(_remoteDesktop.Handle);
            }
            Thread.Sleep(50);
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_C, 0, 0, UIntPtr.Zero);
            keybd_event(VK_C, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("Protocol.VNC", "Failed to send Copy", ex);
        }
    }

    public void SendPaste()
    {
        try
        {
            LogEngine.Instance.Info("Protocol.VNC", "Sending Paste (Ctrl+V) to remote VNC session.");
            try { _remoteDesktop.FillServerClipboard(); } catch { }
            _host.Focus();
            if (_remoteDesktop.IsHandleCreated)
            {
                SetFocus(_remoteDesktop.Handle);
            }
            Thread.Sleep(50);
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("Protocol.VNC", "Failed to send Paste", ex);
        }
    }

    public void ToggleScale()
    {
        _scaled = !_scaled;
        _remoteDesktop.Scaled = _scaled;
        _scaleButton.Content = _scaled ? "Scale: On" : "Scale: Off";
        LogEngine.Instance.Debug("Protocol.VNC", $"Toggled VNC scale to {_scaled}.");
    }

    private void OnConnectComplete(object? sender, ConnectEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var dims = _remoteDesktop.Desktop != null ? $"{_remoteDesktop.Desktop.Width}x{_remoteDesktop.Desktop.Height}" : "Active";
            LogEngine.Instance.Info("Protocol.VNC", $"VNC connected successfully to {_remoteDesktop.Hostname ?? "Host"} ({dims})");
            _statusTextBlock.Text = $"Connected to {_remoteDesktop.Hostname ?? "VNC Host"} ({dims})";
            Connected?.Invoke();
        });
    }

    private void OnConnectionLost(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            LogEngine.Instance.Warn("Protocol.VNC", $"VNC connection closed / lost to {_remoteDesktop.Hostname ?? "Host"}");
            _statusTextBlock.Text = "Connection closed";
            Disconnected?.Invoke();
        });
    }

    public void RefreshDesktop()
    {
        try
        {
            if (_remoteDesktop != null)
            {
                _remoteDesktop.Invalidate();
                _remoteDesktop.Update();
                if (_remoteDesktop.IsConnected)
                {
                    _remoteDesktop.FullScreenUpdate();
                }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            Disconnect();
            _remoteDesktop.Dispose();
            _host.Dispose();
        }
    }
}
