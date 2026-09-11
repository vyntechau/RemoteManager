namespace RemoteManager.Protocols.Vnc;

public class VncClientInfo
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
            if (Id == "BuiltIn") return "Built-in VNC Viewer (In-App Tab)";
            if (Id == "Auto") return "Auto-Detect External Viewer";
            if (Id == "Custom") return "Custom External Executable...";
            return IsInstalled ? $"{DisplayName} (Installed)" : $"{DisplayName} (Not Detected)";
        }
    }

    public override string ToString() => FormattedName;
}
