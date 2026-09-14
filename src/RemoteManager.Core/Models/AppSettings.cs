namespace RemoteManager.Core.Models;

public class AppSettings
{
    public DisplayMode DefaultDisplayMode { get; set; } = DisplayMode.Tabbed;
    public string Theme { get; set; } = "System"; // "System", "Dark", "Light"
    public bool RequireMasterPassword { get; set; } = false;
    public string? MasterPasswordHash { get; set; }
    public string? MasterPasswordSalt { get; set; }
    public bool AutoReconnect { get; set; } = false;
    public bool WarnBeforeDisconnect { get; set; } = true;
    public string SshClientType { get; set; } = "Auto"; // "Auto", "WindowsTerminal", "OpenSSH", "PuTTY", "MobaXterm", "GitBash", "Bitvise", "Custom"
    public string? CustomSshClientPath { get; set; }
    public string? CustomSshClientArgs { get; set; } = "{user}@{host} -p {port}";

    public string VncClientType { get; set; } = "BuiltIn"; // "BuiltIn", "Auto", "Custom"
    public string? CustomVncClientPath { get; set; }
    public string? CustomVncClientArgs { get; set; } = "{host}:{port}";

    // Logging Configuration
    public bool IsLoggingEnabled { get; set; } = true;
    public string MinimumLogLevel { get; set; } = "Debug"; // "Trace", "Debug", "Info", "Warn", "Error"

    // Per-module logging options
    public bool LogAppLifecycle { get; set; } = true;
    public bool LogDatabase { get; set; } = true;
    public bool LogRdp { get; set; } = true;
    public bool LogSsh { get; set; } = true;
    public bool LogVnc { get; set; } = true;
    public bool LogWeb { get; set; } = true;
    public bool LogSecurity { get; set; } = true;

    // Update Configuration
    public bool AutoCheckForUpdates { get; set; } = true;
    public bool CheckPrereleases { get; set; } = false;
    public DateTime? LastUpdateCheckTime { get; set; }
}
