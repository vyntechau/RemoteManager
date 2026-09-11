using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RemoteManager.App.ViewModels;
using RemoteManager.Core.Models;
using RemoteManager.Protocols.Rdp;
using RemoteManager.Protocols.Vnc;
using MediaColor = System.Windows.Media.Color;

namespace RemoteManager.App.Views;

public partial class FullscreenSessionWindow : Window
{
    private readonly SessionTabViewModel _session;
    private readonly Action<SessionTabViewModel> _onExitFullscreen;
    private bool _isClosingFromRestore = false;

    public FullscreenSessionWindow(SessionTabViewModel session, Action<SessionTabViewModel> onExitFullscreen)
    {
        InitializeComponent();
        _session = session;
        _onExitFullscreen = onExitFullscreen;

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

        // Hide inner toolbar so we don't have duplicate action buttons in fullscreen
        if (_session.Content is RdpHostControl rdp)
        {
            rdp.IsToolbarVisible = false;
        }
        else if (_session.Content is VncHostControl vnc)
        {
            vnc.IsToolbarVisible = false;
        }

        SessionHostContainer.Content = session.Content;

        Closing += (s, e) =>
        {
            if (!_isClosingFromRestore)
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

    private void OnWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.F11 || e.Key == Key.Escape)
        {
            ExitFullscreen();
        }
    }

    private void OnExitFullscreenClick(object sender, RoutedEventArgs e)
    {
        ExitFullscreen();
    }

    private void ExitFullscreen()
    {
        _isClosingFromRestore = true;

        // Restore child toolbar when returning to tabbed mode
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
        _onExitFullscreen.Invoke(_session);
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
