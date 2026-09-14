using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using RemoteManager.App.ViewModels;

namespace RemoteManager.App.Views;

public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();

        Loaded += (s, e) =>
        {
            if (DataContext == null && Application.Current?.MainWindow is MainWindow mw)
            {
                DataContext = mw.DataContext;
            }
        };
    }

    private void OnHyperlinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        OpenUrl(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    private void OnOpenWebsiteClick(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://www.vyntech.com.au");
    }

    private void OnOpenTermsClick(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://www.vyntech.com.au/legal/terms");
    }

    private void OnOpenPrivacyClick(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://www.vyntech.com.au/legal/privacy");
    }

    private void OnOpenContactClick(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://www.vyntech.com.au/contact");
    }

    private void OnOpenDocsClick(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://www.vyntech.com.au/documents");
    }

    private void OnOpenGitHubClick(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/vyntechau/RemoteManager");
    }

    private void OnOpenGitHubOrgClick(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/vyntechau");
    }

    private void OnReportIssueClick(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/vyntechau/RemoteManager/issues");
    }

    private void OnEmailSecurityClick(object sender, RoutedEventArgs e)
    {
        OpenUrl("mailto:security@vyntech.com.au");
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

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open link: {ex.Message}", "Navigation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
