using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteManager.Core.Models;

namespace RemoteManager.App.ViewModels;

public partial class SessionTabViewModel : ObservableObject
{
    [ObservableProperty]
    private Guid _id = Guid.NewGuid();

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private ProtocolType _protocol;

    [ObservableProperty]
    private string _status = "Connecting";

    [ObservableProperty]
    private bool _isConnected = false;

    [ObservableProperty]
    private bool _isSelected = false;

    [ObservableProperty]
    private object? _content;

    [ObservableProperty]
    private ConnectionItem _connection;

    /// <summary>
    /// True if this tab is a utility/page tab (Settings, Logs, About, Connections Hub)
    /// rather than a remote session.
    /// </summary>
    [ObservableProperty]
    private bool _isUtilityTab = false;

    /// <summary>
    /// Tag identifying the utility tab type: "Settings", "Logs", "About", "Connections".
    /// Used for deduplication and icon selection.
    /// </summary>
    [ObservableProperty]
    private string _utilityTabTag = string.Empty;

    public Func<SessionTabViewModel, Task>? OnCloseRequested { get; set; }
    public Action<SessionTabViewModel>? OnPopOutRequested { get; set; }
    public Action<SessionTabViewModel>? OnFullscreenRequested { get; set; }

    /// <summary>
    /// Constructor for remote session tabs.
    /// </summary>
    public SessionTabViewModel(ConnectionItem connection, object? content)
    {
        _connection = connection;
        _title = connection.DisplayName;
        _protocol = connection.Protocol;
        _content = content;
    }

    /// <summary>
    /// Constructor for utility/page tabs (Settings, Logs, About, Connections Hub).
    /// </summary>
    public SessionTabViewModel(string title, string utilityTag, object? content)
    {
        _connection = new ConnectionItem { Name = title };
        _title = title;
        _content = content;
        _isUtilityTab = true;
        _utilityTabTag = utilityTag;
        _status = string.Empty;
        _isConnected = false;
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        if (OnCloseRequested != null)
            await OnCloseRequested.Invoke(this);
    }

    [RelayCommand]
    private void PopOut()
    {
        OnPopOutRequested?.Invoke(this);
    }

    [RelayCommand]
    private void Fullscreen()
    {
        OnFullscreenRequested?.Invoke(this);
    }
}
