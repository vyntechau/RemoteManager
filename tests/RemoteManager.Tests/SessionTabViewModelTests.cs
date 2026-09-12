using RemoteManager.App.ViewModels;
using RemoteManager.Core.Models;
using Xunit;

namespace RemoteManager.Tests;

public class SessionTabViewModelTests
{
    [Fact]
    public void SessionTabViewModel_RemoteSessionConstructor_InitializesCorrectly()
    {
        var conn = new ConnectionItem
        {
            Name = "Production Gateway",
            Host = "10.0.0.1",
            Protocol = ProtocolType.SSH
        };
        var dummyContent = new object();

        var tab = new SessionTabViewModel(conn, dummyContent);

        Assert.Equal("Production Gateway", tab.Title);
        Assert.Equal(ProtocolType.SSH, tab.Protocol);
        Assert.Same(dummyContent, tab.Content);
        Assert.False(tab.IsUtilityTab);
        Assert.Equal("Connecting", tab.Status);
        Assert.False(tab.IsConnected);
        Assert.False(tab.IsSelected);
        Assert.NotEqual(Guid.Empty, tab.Id);
    }

    [Fact]
    public void SessionTabViewModel_UtilityTabConstructor_InitializesCorrectly()
    {
        var dummyContent = new object();

        var tab = new SessionTabViewModel("Application Settings", "Settings", dummyContent);

        Assert.Equal("Application Settings", tab.Title);
        Assert.True(tab.IsUtilityTab);
        Assert.Equal("Settings", tab.UtilityTabTag);
        Assert.Same(dummyContent, tab.Content);
        Assert.Equal(string.Empty, tab.Status);
        Assert.False(tab.IsConnected);
    }

    [Fact]
    public async Task SessionTabViewModel_CloseCommand_InvokesCallback()
    {
        var conn = new ConnectionItem { Name = "Web Server", Protocol = ProtocolType.Web };
        var tab = new SessionTabViewModel(conn, null);

        SessionTabViewModel? closedTab = null;
        tab.OnCloseRequested = t =>
        {
            closedTab = t;
            return Task.CompletedTask;
        };

        await tab.CloseCommand.ExecuteAsync(null);

        Assert.Same(tab, closedTab);
    }

    [Fact]
    public void SessionTabViewModel_PopOutAndFullscreenCommands_InvokeCallbacks()
    {
        var conn = new ConnectionItem { Name = "VNC Node", Protocol = ProtocolType.VNC };
        var tab = new SessionTabViewModel(conn, null);

        SessionTabViewModel? popOutTab = null;
        SessionTabViewModel? fullscreenTab = null;

        tab.OnPopOutRequested = t => popOutTab = t;
        tab.OnFullscreenRequested = t => fullscreenTab = t;

        tab.PopOutCommand.Execute(null);
        Assert.Same(tab, popOutTab);

        tab.FullscreenCommand.Execute(null);
        Assert.Same(tab, fullscreenTab);
    }
}
