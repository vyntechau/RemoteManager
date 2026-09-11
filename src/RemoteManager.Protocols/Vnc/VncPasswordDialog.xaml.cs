using System.Windows;

namespace RemoteManager.Protocols.Vnc;

public partial class VncPasswordDialog : Window
{
    public string? EnteredPassword { get; private set; }

    public VncPasswordDialog(string? serverInfo = null)
    {
        InitializeComponent();

        if (!string.IsNullOrWhiteSpace(serverInfo))
        {
            ServerInfoText.Text = $"The VNC server at {serverInfo} requires a password to connect.";
        }

        Loaded += (s, e) => PasswordInput.Focus();
    }

    public void CenterOnElement(FrameworkElement element)
    {
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Loaded += (s, e) =>
        {
            try
            {
                var owner = Owner ?? Window.GetWindow(element);
                if (owner != null && element.IsLoaded && element.IsVisible && element.ActualWidth > 0 && element.ActualHeight > 0)
                {
                    var relativePoint = element.TransformToAncestor(owner).Transform(new System.Windows.Point(0, 0));
                    var targetLeft = owner.Left + relativePoint.X + (element.ActualWidth - ActualWidth) / 2.0;
                    var targetTop = owner.Top + relativePoint.Y + (element.ActualHeight - ActualHeight) / 2.0;

                    if (!double.IsNaN(targetLeft) && !double.IsNaN(targetTop) && !double.IsInfinity(targetLeft) && !double.IsInfinity(targetTop))
                    {
                        Left = targetLeft;
                        Top = targetTop;
                    }
                }
            }
            catch
            {
                // Native CenterOwner handles placement automatically as fallback
            }
        };
    }

    private void OnConnectClick(object sender, RoutedEventArgs e)
    {
        EnteredPassword = PasswordInput.Password;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        EnteredPassword = null;
        DialogResult = false;
        Close();
    }
}
