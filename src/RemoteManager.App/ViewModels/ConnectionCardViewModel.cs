using System.Diagnostics;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteManager.Core.Logging;
using RemoteManager.Core.Models;
using ProtocolType = RemoteManager.Core.Models.ProtocolType;

namespace RemoteManager.App.ViewModels;

public enum ConnectionPingStatus
{
    Untested,
    Testing,
    Online,
    Offline
}

public partial class ConnectionCardViewModel : ObservableObject
{
    public ConnectionItem Model { get; }

    [ObservableProperty]
    private string _credentialTitle = "(None / Prompt)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PingStatusText))]
    [NotifyPropertyChangedFor(nameof(PingStatusColor))]
    [NotifyPropertyChangedFor(nameof(IsOnline))]
    [NotifyPropertyChangedFor(nameof(IsOffline))]
    private ConnectionPingStatus _pingStatus = ConnectionPingStatus.Untested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PingStatusText))]
    private int? _latencyMs;

    [ObservableProperty]
    private bool _isPinging;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BookmarkTooltip))]
    [NotifyPropertyChangedFor(nameof(BookmarkIconColor))]
    private bool _isBookmarked;

    public int SortOrder
    {
        get => Model.SortOrder;
        set
        {
            if (Model.SortOrder != value)
            {
                Model.SortOrder = value;
                OnPropertyChanged();
            }
        }
    }

    public string BookmarkTooltip => IsBookmarked ? "Remove Bookmark" : "Bookmark this server (pin to top)";
    public string BookmarkIconColor => IsBookmarked ? "#FFB900" : "#8A8886";

    partial void OnIsBookmarkedChanged(bool value)
    {
        Model.IsBookmarked = value;
    }

    public Guid Id => Model.Id;
    public string DisplayName => Model.DisplayName;
    public string Name => Model.Name;
    public string Host => Model.Host;
    public int Port => Model.Port;
    public string FullAddress => Model.FullAddress;
    public ProtocolType Protocol => Model.Protocol;
    public DisplayMode DisplayMode => Model.DisplayMode;
    public DateTime CreatedAt => Model.CreatedAt;
    public DateTime UpdatedAt => Model.UpdatedAt;

    public bool IsOnline => PingStatus == ConnectionPingStatus.Online;
    public bool IsOffline => PingStatus == ConnectionPingStatus.Offline;

    public string DisplayModeText => Model.DisplayMode switch
    {
        DisplayMode.Tabbed => "Embedded Tab",
        DisplayMode.Fullscreen => "Fullscreen (F11)",
        DisplayMode.ExternalApp => "Native Window",
        _ => Model.DisplayMode.ToString()
    };

    public string ProtocolBadgeColor => Model.Protocol switch
    {
        ProtocolType.RDP => "#0078D4",
        ProtocolType.SSH => "#107C41",
        ProtocolType.VNC => "#D83B01",
        ProtocolType.Web => "#881798",
        _ => "#0078D4"
    };

    public string ProtocolBadgeLightBg => Model.Protocol switch
    {
        ProtocolType.RDP => "#200078D4",
        ProtocolType.SSH => "#20107C41",
        ProtocolType.VNC => "#20D83B01",
        ProtocolType.Web => "#20881798",
        _ => "#200078D4"
    };

    public string PingStatusText => PingStatus switch
    {
        ConnectionPingStatus.Testing => "Testing...",
        ConnectionPingStatus.Online => LatencyMs.HasValue ? $"{LatencyMs} ms" : "Online",
        ConnectionPingStatus.Offline => "Unreachable",
        _ => "Untested"
    };

    public string PingStatusColor => PingStatus switch
    {
        ConnectionPingStatus.Online => "#107C41",
        ConnectionPingStatus.Offline => "#E81123",
        ConnectionPingStatus.Testing => "#FFB900",
        _ => "#8A8886"
    };

    public ConnectionCardViewModel(ConnectionItem model, string? credentialTitle = null)
    {
        Model = model;
        IsBookmarked = model.IsBookmarked;
        if (!string.IsNullOrWhiteSpace(credentialTitle))
        {
            CredentialTitle = credentialTitle;
        }
    }

    [RelayCommand]
    public async Task PingAsync()
    {
        if (IsPinging) return;

        IsPinging = true;
        PingStatus = ConnectionPingStatus.Testing;
        LatencyMs = null;

        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(2500));
            using var client = new TcpClient();
            
            await client.ConnectAsync(Model.Host, Model.Port, cts.Token);

            sw.Stop();
            LatencyMs = Math.Max(1, (int)sw.ElapsedMilliseconds);
            PingStatus = ConnectionPingStatus.Online;
            LogEngine.Instance.Debug("Network", $"Ping to {Model.Host}:{Model.Port} succeeded in {LatencyMs}ms");
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            PingStatus = ConnectionPingStatus.Offline;
            LogEngine.Instance.Debug("Network", $"Ping to {Model.Host}:{Model.Port} timed out");
        }
        catch (Exception ex)
        {
            sw.Stop();
            PingStatus = ConnectionPingStatus.Offline;
            LogEngine.Instance.Debug("Network", $"Ping to {Model.Host}:{Model.Port} error: {ex.Message}");
        }
        finally
        {
            IsPinging = false;
        }
    }
}
