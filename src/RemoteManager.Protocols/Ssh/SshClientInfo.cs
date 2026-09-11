namespace RemoteManager.Protocols.Ssh;

public class SshClientInfo
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? ExecutablePath { get; set; }
    public bool IsInstalled { get; set; }
    public string DefaultArgsTemplate { get; set; } = string.Empty;

    public string FormattedName
    {
        get
        {
            if (Id == "Auto") return "Auto-Detect (Windows Terminal / OpenSSH)";
            if (Id == "Custom") return "Custom Executable / Client...";
            return IsInstalled ? $"{DisplayName} (Installed)" : $"{DisplayName} (Not Detected)";
        }
    }

    public override string ToString() => FormattedName;
}
