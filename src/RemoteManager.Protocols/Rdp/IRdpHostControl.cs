namespace RemoteManager.Protocols.Rdp;

public interface IRdpHostControl
{
    event Action? DisconnectRequested;
    void Connect(string server, int port, string? username, string? domain, string? password, int width = 1920, int height = 1080);
    void Disconnect();
}
