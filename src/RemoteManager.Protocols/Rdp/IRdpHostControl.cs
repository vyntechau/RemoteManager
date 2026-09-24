namespace RemoteManager.Protocols.Rdp;

public interface IRdpHostControl
{
    event Action? DisconnectRequested;
    event Action<string, int, int>? Disconnected;
    event Action? Connected;
    void Connect(string server, int port, string? username, string? domain, string? password, int width = 1920, int height = 1080);
    void Disconnect();
    void FocusRdp() { }
    void SendCopy() { }
    void SendPaste() { }
    void SendCtrlAltDel() { }
}
