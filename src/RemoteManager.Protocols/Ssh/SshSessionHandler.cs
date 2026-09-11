using System.Diagnostics;
using System.IO;
using RemoteManager.Core.Models;
using RemoteManager.Core.Logging;

namespace RemoteManager.Protocols.Ssh;

public static class SshSessionHandler
{
    private static readonly char[] DisallowedChars = ['&', '|', ';', '`', '$', '<', '>', '"', '\'', '\r', '\n'];

    public static Process Launch(string host, int port, string? username, AppSettings? settings = null)
    {
        var sshPort = port > 0 ? port : 22;
        if (sshPort < 1 || sshPort > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(host) || host.IndexOfAny(DisallowedChars) >= 0)
        {
            throw new ArgumentException("Host contains invalid characters or is empty.", nameof(host));
        }

        var user = !string.IsNullOrWhiteSpace(username) ? username.Trim() : Environment.UserName;
        if (user.IndexOfAny(DisallowedChars) >= 0 || user.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Username contains invalid characters.", nameof(username));
        }

        var userParam = !string.IsNullOrWhiteSpace(username) ? $"{user}@{host.Trim()}" : host.Trim();
        var clientType = settings?.SshClientType ?? "Auto";
        LogEngine.Instance.Info("Protocol.SSH", $"Launching SSH session to {userParam}:{sshPort} (Client mode: {clientType})");

        // Custom Client
        if (clientType == "Custom" && !string.IsNullOrWhiteSpace(settings?.CustomSshClientPath) && File.Exists(settings.CustomSshClientPath))
        {
            var args = FormatArguments(settings.CustomSshClientArgs ?? "{user}@{host} -p {port}", host.Trim(), sshPort, user);
            LogEngine.Instance.Debug("Protocol.SSH", $"Using custom SSH client at {settings.CustomSshClientPath}");
            return LaunchProcess(settings.CustomSshClientPath, args);
        }

        // Detected specific clients
        if (clientType != "Auto")
        {
            var detected = SshClientDetector.DetectAvailableClients().FirstOrDefault(c => c.Id == clientType);
            var exePath = (detected != null && detected.IsInstalled && !string.IsNullOrEmpty(detected.ExecutablePath))
                ? detected.ExecutablePath
                : settings?.CustomSshClientPath;

            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                var template = !string.IsNullOrEmpty(settings?.CustomSshClientArgs)
                    ? settings.CustomSshClientArgs
                    : detected?.DefaultArgsTemplate ?? "{user}@{host} -p {port}";
                var args = FormatArguments(template, host.Trim(), sshPort, user);
                LogEngine.Instance.Debug("Protocol.SSH", $"Using {detected?.DisplayName ?? clientType} client at {exePath}");
                return LaunchProcess(exePath, args);
            }
        }

        // Auto-detect default: Windows Terminal or fallback to cmd / ssh
        var wtPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\WindowsApps\wt.exe");

        if (File.Exists(wtPath))
        {
            LogEngine.Instance.Debug("Protocol.SSH", $"Using Windows Terminal (wt.exe) for SSH to {userParam}");
            return LaunchProcess(wtPath, $"--title \"SSH: {userParam}\" ssh -p {sshPort} {userParam}");
        }

        LogEngine.Instance.Debug("Protocol.SSH", $"Falling back to cmd.exe OpenSSH for {userParam}");
        return LaunchProcess("cmd.exe", $"/k ssh -p {sshPort} {userParam}");
    }

    private static string FormatArguments(string template, string host, int port, string user)
    {
        return template
            .Replace("{host}", host, StringComparison.OrdinalIgnoreCase)
            .Replace("{port}", port.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{user}", user, StringComparison.OrdinalIgnoreCase);
    }

    private static Process LaunchProcess(string fileName, string arguments)
    {
        LogEngine.Instance.Info("Protocol.SSH", $"Starting external process '{fileName}' with arguments: {arguments}");
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = true
        };

        try
        {
            var proc = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start SSH client: {fileName}");

            proc.EnableRaisingEvents = true;
            proc.Exited += (s, e) =>
            {
                try
                {
                    LogEngine.Instance.Info("Protocol.SSH", $"SSH client process '{fileName}' exited with code {proc.ExitCode}");
                }
                catch { }
            };

            return proc;
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("Protocol.SSH", $"Failed to execute SSH client '{fileName}'", ex);
            throw;
        }
    }
}
