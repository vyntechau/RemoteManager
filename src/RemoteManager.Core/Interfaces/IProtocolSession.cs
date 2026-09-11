using RemoteManager.Core.Models;

namespace RemoteManager.Core.Interfaces;

public enum SessionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Failed
}

public interface IProtocolSession : IDisposable
{
    Guid Id { get; }
    ConnectionItem Connection { get; }
    SessionState State { get; }
    string? StatusMessage { get; }

    event EventHandler<SessionState>? StateChanged;

    Task ConnectAsync(Credential? credential);
    Task DisconnectAsync();
}
