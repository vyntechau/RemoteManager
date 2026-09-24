using System.Diagnostics;
using System.IO;
using System.Text;
using RemoteManager.Core.Logging;

namespace RemoteManager.Protocols.Rdp;

public static class RdpIsolatedLauncher
{
    internal static Func<ProcessStartInfo, Process?> ProcessLauncher { get; set; } = psi => Process.Start(psi);

    public static Process Launch(
        string host,
        int port,
        string? username,
        string? domain,
        string? password,
        bool fullScreen = false)
    {
        var targetHost = port > 0 && port != 3389 ? $"{host}:{port}" : host;
        LogEngine.Instance.Info("Protocol.RDP", $"Launching isolated external RDP (mstsc.exe) to {targetHost} (User: '{username}', FullScreen: {fullScreen})");

        var injected = false;
        // If credentials are provided, register them via cmdkey for this target
        if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrEmpty(password))
        {
            var userString = string.IsNullOrWhiteSpace(domain) ? username : $"{domain}\\{username}";
            LogEngine.Instance.Debug("Protocol.RDP", $"Injecting temporary Windows vault credential for target {host} (User: {userString})");
            InjectCredential(host, userString, password);
            injected = true;
        }

        // Generate temporary isolated .rdp file
        var rdpFile = GenerateTempRdpFile(targetHost, username, domain, fullScreen);
        LogEngine.Instance.Debug("Protocol.RDP", $"Generated temporary .rdp file at {rdpFile}");

        var startInfo = new ProcessStartInfo
        {
            FileName = "mstsc.exe",
            Arguments = fullScreen ? $"\"{rdpFile}\" /f" : $"\"{rdpFile}\"",
            UseShellExecute = true
        };

        var process = ProcessLauncher(startInfo)
            ?? throw new InvalidOperationException("Failed to launch mstsc.exe");

        // Clean up temp file and temporary credentials when process exits
        process.EnableRaisingEvents = true;
        process.Exited += (s, e) => CleanupProcessExit(targetHost, rdpFile, injected, host);

        return process;
    }

    internal static void CleanupProcessExit(string targetHost, string rdpFile, bool injected, string host)
    {
        LogEngine.Instance.Info("Protocol.RDP", $"mstsc.exe process exited for {targetHost}. Cleaning up temporary files and vault credentials.");
        try
        {
            if (File.Exists(rdpFile))
            {
                File.Delete(rdpFile);
            }
        }
        catch { }

        if (injected)
        {
            PurgeCredential(host);
        }
    }

    internal static void InjectCredential(string target, string username, string password)
    {
        try
        {
            var sanitizedTarget = target.Replace("\"", "").Trim();
            var safeUser = username.Replace("\"", "\\\"");
            var safePass = password.Replace("\"", "\\\"");

            var psi = new ProcessStartInfo
            {
                FileName = "cmdkey.exe",
                Arguments = $"/generic:TERMSRV/{sanitizedTarget} /user:\"{safeUser}\" /pass:\"{safePass}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            using var proc = ProcessLauncher(psi);
            proc?.WaitForExit(3000);
        }
        catch
        {
            // Silently continue if cmdkey is restricted
        }
    }

    internal static void PurgeCredential(string target)
    {
        try
        {
            var sanitizedTarget = target.Replace("\"", "").Trim();
            var psi = new ProcessStartInfo
            {
                FileName = "cmdkey.exe",
                Arguments = $"/delete:TERMSRV/{sanitizedTarget}",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            using var proc = ProcessLauncher(psi);
            proc?.WaitForExit(3000);
        }
        catch
        {
            // Silently continue if cmdkey is restricted
        }
    }

    internal static string GenerateTempRdpFile(string targetHost, string? username, string? domain, bool fullScreen)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"RemoteManager_{Guid.NewGuid():N}.rdp");

        var sb = new StringBuilder();
        sb.AppendLine($"full address:s:{targetHost}");
        if (!string.IsNullOrWhiteSpace(username))
        {
            sb.AppendLine($"username:s:{username}");
        }
        if (!string.IsNullOrWhiteSpace(domain))
        {
            sb.AppendLine($"domain:s:{domain}");
        }

        sb.AppendLine("screen mode id:i:" + (fullScreen ? "2" : "1"));
        sb.AppendLine("use multimon:i:0");
        sb.AppendLine("desktopwidth:i:1920");
        sb.AppendLine("desktopheight:i:1080");
        sb.AppendLine("session bpp:i:32");
        sb.AppendLine("compression:i:1");
        sb.AppendLine("keyboardhook:i:1");
        sb.AppendLine("audiomode:i:0");
        sb.AppendLine("redirectprinters:i:0");
        sb.AppendLine("redirectcomports:i:0");
        sb.AppendLine("redirectsmartcards:i:0");
        sb.AppendLine("redirectclipboard:i:1");
        sb.AppendLine("redirectposdevices:i:0");
        sb.AppendLine("redirectdrives:i:1");
        sb.AppendLine("drivestoredirect:s:*");
        sb.AppendLine("autoreconnection enabled:i:1");
        sb.AppendLine("authentication level:i:2");
        sb.AppendLine("prompt for credentials:i:0");
        sb.AppendLine("negotiate security layer:i:1");
        sb.AppendLine("enablecredsspsupport:i:1");
        sb.AppendLine("smart sizing:i:1");

        File.WriteAllText(tempFile, sb.ToString(), Encoding.Unicode);
        return tempFile;
    }
}
