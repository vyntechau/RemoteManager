namespace RemoteManager.Protocols.Vnc;

public interface IVncHostControl
{
    event Action? Connected;
    event Action? Disconnected;
    event Action<string>? Error;
    void Connect(string host, int port, string? password);
    void Disconnect();
    void Dispose();
}
