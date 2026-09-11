using System.IO;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using RemoteManager.Core.Logging;

namespace RemoteManager.Protocols.Web;

public class WebView2SessionControl : ContentControl
{
    private readonly WebView2 _webView;
    private readonly Guid _connectionId;

    public WebView2SessionControl(Guid connectionId)
    {
        _connectionId = connectionId;
        _webView = new WebView2();
        Content = _webView;

        _webView.NavigationStarting += (s, e) =>
        {
            LogEngine.Instance.Debug("Protocol.Web", $"Navigation starting: {e.Uri}");
        };

        _webView.NavigationCompleted += (s, e) =>
        {
            if (e.IsSuccess)
            {
                LogEngine.Instance.Info("Protocol.Web", $"Navigation completed successfully: {_webView.Source}");
            }
            else
            {
                LogEngine.Instance.Warn("Protocol.Web", $"Navigation failed for {_webView.Source}: WebErrorStatus = {e.WebErrorStatus}");
            }
        };
    }

    public async Task NavigateAsync(string url)
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        // Each connection gets its own isolated UserDataFolder
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profileDir = Path.Combine(appData, "RemoteManager", "WebProfiles", _connectionId.ToString("N"));
        Directory.CreateDirectory(profileDir);

        LogEngine.Instance.Info("Protocol.Web", $"Initializing WebView2 profile at '{profileDir}' for target: {url}");
        var env = await CoreWebView2Environment.CreateAsync(null, profileDir);
        await _webView.EnsureCoreWebView2Async(env);

        if (_webView.CoreWebView2 != null)
        {
            // Allow self-signed certificates commonly found on internal routers, switches, and hypervisor consoles
            _webView.CoreWebView2.ServerCertificateErrorDetected += (s, e) =>
            {
                LogEngine.Instance.Warn("Protocol.Web", $"Server certificate warning for {e.RequestUri}: {e.ErrorStatus}. Allowed for remote session.");
                e.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
            };
        }

        LogEngine.Instance.Debug("Protocol.Web", $"CoreWebView2 initialized. Setting source to {url}");
        _webView.Source = new Uri(url);
    }
}
