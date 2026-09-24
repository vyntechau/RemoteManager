using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RemoteManager.App.ViewModels;
using RemoteManager.Core.Models;
using RemoteManager.Protocols.Rdp;
using RemoteManager.Protocols.Vnc;
using MediaColor = System.Windows.Media.Color;

namespace RemoteManager.App.Views;

public partial class DetachedSessionWindow : Window
{
    private readonly SessionTabViewModel _session;
    private readonly Action<SessionTabViewModel> _onDockBack;
    private bool _isClosingFromDock = false;

    public DetachedSessionWindow(SessionTabViewModel session, Action<SessionTabViewModel> onDockBack)
    {
        InitializeComponent();
        _session = session;
        _onDockBack = onDockBack;

        Title = session.Title;
        SessionTitleText.Text = session.Title;
        ProtocolText.Text = session.Protocol.ToString().ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(session.Connection?.Host))
        {
            var portStr = session.Connection.Port > 0 ? $":{session.Connection.Port}" : "";
            SessionAddressText.Text = $"({session.Connection.Host}{portStr})";
        }

        ProtocolBadge.Background = session.Protocol switch
        {
            ProtocolType.RDP => new SolidColorBrush(MediaColor.FromRgb(0, 120, 212)),
            ProtocolType.VNC => new SolidColorBrush(MediaColor.FromRgb(216, 59, 1)),
            ProtocolType.SSH => new SolidColorBrush(MediaColor.FromRgb(16, 124, 65)),
            ProtocolType.Web => new SolidColorBrush(MediaColor.FromRgb(135, 100, 184)),
            _ => new SolidColorBrush(MediaColor.FromRgb(100, 100, 100))
        };

        // Hide child toolbar so we don't have duplicate action bars
        if (_session.Content is RdpHostControl rdp)
        {
            rdp.IsToolbarVisible = false;
        }
        else if (_session.Content is VncHostControl vnc)
        {
            vnc.IsToolbarVisible = false;
        }

        // Transfer content from session
        SessionHostContainer.Content = session.Content;

        Loaded += (s, e) =>
        {
            if (_session.Content is IRdpHostControl rdp)
            {
                Dispatcher.BeginInvoke(new Action(() => rdp.FocusRdp()), System.Windows.Threading.DispatcherPriority.Input);
            }
        };

        Activated += (s, e) =>
        {
            if (_session.Content is IRdpHostControl rdp)
            {
                Dispatcher.BeginInvoke(new Action(() => rdp.FocusRdp()), System.Windows.Threading.DispatcherPriority.Input);
            }
        };

        Closing += (s, e) =>
        {
            if (!_isClosingFromDock)
            {
                if (_session.Content is RdpHostControl rdp)
                {
                    rdp.Disconnect();
                }
                else if (_session.Content is VncHostControl vnc)
                {
                    vnc.Disconnect();
                }
            }
        };

        Closed += (s, e) =>
        {
            // Restore inner toolbar when window closes
            if (_session.Content is RdpHostControl rdp)
            {
                rdp.IsToolbarVisible = true;
            }
            else if (_session.Content is VncHostControl vnc)
            {
                vnc.IsToolbarVisible = true;
            }
        };
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            OnFullscreenClick(sender, e);
        }
        else if (e.Key == Key.Escape && WindowStyle == WindowStyle.None)
        {
            OnFullscreenClick(sender, e);
        }
    }

    private void OnCadClick(object sender, RoutedEventArgs e)
    {
        if (_session.Content is RdpHostControl rdp)
        {
            rdp.SendCtrlAltDel();
        }
        else if (_session.Content is VncHostControl vnc)
        {
            vnc.SendCtrlAltDel();
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (_session.Content is RdpHostControl rdp)
        {
            rdp.SendCopy();
        }
        else if (_session.Content is VncHostControl vnc)
        {
            vnc.SendCopy();
        }
    }

    private void OnPasteClick(object sender, RoutedEventArgs e)
    {
        if (_session.Content is RdpHostControl rdp)
        {
            rdp.SendPaste();
        }
        else if (_session.Content is VncHostControl vnc)
        {
            vnc.SendPaste();
        }
    }

    private void OnDockClick(object sender, RoutedEventArgs e)
    {
        _isClosingFromDock = true;

        // Restore child toolbar when returning to tabbed dock mode
        if (_session.Content is RdpHostControl rdp)
        {
            rdp.IsToolbarVisible = true;
        }
        else if (_session.Content is VncHostControl vnc)
        {
            vnc.IsToolbarVisible = true;
        }

        SessionHostContainer.Content = null;
        Close();
        _onDockBack.Invoke(_session);
    }

    private void OnFullscreenClick(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized && WindowStyle == WindowStyle.None)
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = WindowState.Normal;
            FullscreenButton.Content = "Fullscreen (F11)";
        }
        else
        {
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            FullscreenButton.Content = "Exit Fullscreen (F11)";
        }
    }

    private void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        if (_session.Content is RdpHostControl rdp)
        {
            rdp.Disconnect();
        }
        else if (_session.Content is VncHostControl vnc)
        {
            vnc.Disconnect();
        }
        Close();
    }
}
