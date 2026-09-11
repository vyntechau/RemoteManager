using System.Windows;
using RemoteManager.Core.Models;
using RemoteManager.Protocols.Ssh;
using Xunit;

namespace RemoteManager.Tests;

public class SshDetectorTests
{
    [Fact]
    public void Test_DetectAvailableClients_ReturnsStandardClients()
    {
        // Act
        var clients = SshClientDetector.DetectAvailableClients();

        // Assert
        Assert.NotNull(clients);
        Assert.NotEmpty(clients);

        // Verify Auto and Custom always exist
        var autoClient = clients.FirstOrDefault(c => c.Id == "Auto");
        var customClient = clients.FirstOrDefault(c => c.Id == "Custom");
        var openSshClient = clients.FirstOrDefault(c => c.Id == "OpenSSH");

        Assert.NotNull(autoClient);
        Assert.NotNull(customClient);
        Assert.NotNull(openSshClient);

        Assert.True(autoClient.IsInstalled);
        Assert.True(customClient.IsInstalled);
        Assert.Contains("Auto-Detect", autoClient.FormattedName);
        Assert.Contains("Custom", customClient.FormattedName);
    }

    [Fact]
    public void Test_SshClientInfo_FormattedName_ShowsDetectionState()
    {
        var installed = new SshClientInfo
        {
            Id = "PuTTY",
            DisplayName = "PuTTY",
            ExecutablePath = @"C:\Program Files\PuTTY\putty.exe",
            IsInstalled = true
        };

        var notInstalled = new SshClientInfo
        {
            Id = "PuTTY",
            DisplayName = "PuTTY",
            ExecutablePath = null,
            IsInstalled = false
        };

        Assert.Equal("PuTTY (Installed)", installed.FormattedName);
        Assert.Equal("PuTTY (Not Detected)", notInstalled.FormattedName);
    }

    [Fact]
    public void Test_AppSettings_SshConfiguration_Defaults()
    {
        var settings = new AppSettings();

        Assert.Equal("Auto", settings.SshClientType);
        Assert.Null(settings.CustomSshClientPath);
        Assert.Equal("{user}@{host} -p {port}", settings.CustomSshClientArgs);
    }

    [Fact]
    public void Test_WpfUi_NavigationView_Types_Exist()
    {
        var navViewType = typeof(Wpf.Ui.Controls.NavigationView);
        var navItemType = typeof(Wpf.Ui.Controls.NavigationViewItem);
        var symbolIconType = typeof(Wpf.Ui.Controls.SymbolIcon);
        var infoBadgeType = typeof(Wpf.Ui.Controls.InfoBadge);

        Assert.NotNull(navViewType);
        Assert.NotNull(navItemType);
        Assert.NotNull(symbolIconType);
        Assert.NotNull(infoBadgeType);

        Assert.True(Enum.IsDefined(typeof(Wpf.Ui.Controls.SymbolRegular), "Server24"));
        Assert.True(Enum.IsDefined(typeof(Wpf.Ui.Controls.SymbolRegular), "Key24"));
        Assert.True(Enum.IsDefined(typeof(Wpf.Ui.Controls.SymbolRegular), "Settings24"));
        Assert.True(Enum.IsDefined(typeof(Wpf.Ui.Controls.SymbolRegular), "Info24"));
    }

    [Fact]
    public void Test_WpfUi_NavigationView_Properties()
    {
        var thread = new Thread(() =>
        {
            var nav = new Wpf.Ui.Controls.NavigationView
            {
                PaneDisplayMode = Wpf.Ui.Controls.NavigationViewPaneDisplayMode.Left,
                IsBackButtonVisible = Wpf.Ui.Controls.NavigationViewBackButtonVisible.Collapsed,
                IsPaneToggleVisible = false,
                OpenPaneLength = 340
            };
            var sItem = new Wpf.Ui.Controls.NavigationViewItem
            {
                Content = "Settings",
                Tag = "Settings",
                Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Settings24 }
            };
            var aItem = new Wpf.Ui.Controls.NavigationViewItem
            {
                Content = "About",
                Tag = "About",
                Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Info24 }
            };
            if (Application.Current == null)
            {
                var app = new Application();
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark });
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
            }

            nav.FooterMenuItems.Add(sItem);
            nav.FooterMenuItems.Add(aItem);

            Assert.Equal(2, nav.FooterMenuItems.Count);
            Assert.Equal("Settings", ((Wpf.Ui.Controls.NavigationViewItem)nav.FooterMenuItems[0]!).Content);
            Assert.Equal("About", ((Wpf.Ui.Controls.NavigationViewItem)nav.FooterMenuItems[1]!).Content);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void Test_VncSharp_RemoteDesktop_Instantiates()
    {
        var thread = new Thread(() =>
        {
            var rd = new VncSharpCore.RemoteDesktop();
            Assert.NotNull(rd);
            Assert.Equal(5900, rd.VncPort);

            rd.VncPort = 5901;
            Assert.Equal(5901, rd.VncPort);

            rd.VncPort = 5900;
            Assert.Equal(5900, rd.VncPort);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void Test_VncHostControl_Instantiates()
    {
        var thread = new Thread(() =>
        {
            var host = new RemoteManager.Protocols.Vnc.VncHostControl();
            Assert.NotNull(host);
            Assert.False(host.IsConnected);
            host.Dispose();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void Test_VncSharp_RemoteDesktop_ConnectedState_DoesNotThrowStreamNullException()
    {
        var thread = new Thread(() =>
        {
            var rd = new VncSharpCore.RemoteDesktop();
            var runtimeStateType = typeof(VncSharpCore.RemoteDesktop).Assembly.GetType("VncSharpCore.RemoteDesktop+RuntimeState");
            Assert.NotNull(runtimeStateType);
            var setStateMethod = typeof(VncSharpCore.RemoteDesktop).GetMethod(
                "SetState",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new[] { runtimeStateType },
                null);
            Assert.NotNull(setStateMethod);
            var connectedValue = Enum.Parse(runtimeStateType, "Connected");

            // Calling SetState(RuntimeState.Connected) previously threw ArgumentNullException: Value cannot be null (Parameter 'stream')
            var ex = Record.Exception(() => setStateMethod.Invoke(rd, new[] { connectedValue }));
            Assert.Null(ex);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void Test_VncHostControl_Connect_ValidatesPort_NoArgumentException()
    {
        var thread = new Thread(() =>
        {
            var host = new RemoteManager.Protocols.Vnc.VncHostControl();
            string? capturedError = null;
            host.Error += err => capturedError = err;

            // Connect to 127.0.0.1 with port 5900
            host.Connect("127.0.0.1", 5900);
            Assert.False(capturedError?.Contains("Parameter 'port'") == true);

            // Connect with host:port string
            capturedError = null;
            host.Connect("127.0.0.1:5901", 5900);
            Assert.False(capturedError?.Contains("Parameter 'port'") == true);

            host.Dispose();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void Test_VncSessionHandler_DetectAvailableClients()
    {
        var clients = RemoteManager.Protocols.Vnc.VncSessionHandler.DetectAvailableClients();
        Assert.NotNull(clients);
        Assert.True(clients.Count >= 3);

        var builtIn = clients.FirstOrDefault(c => c.Id == "BuiltIn");
        var auto = clients.FirstOrDefault(c => c.Id == "Auto");
        var custom = clients.FirstOrDefault(c => c.Id == "Custom");

        Assert.NotNull(builtIn);
        Assert.NotNull(auto);
        Assert.NotNull(custom);

        Assert.True(builtIn.IsInstalled);
        Assert.Contains("Built-in", builtIn.FormattedName);
    }

    [Fact]
    public void Test_VncPasswordDialog_StartupLocation_IsCenterOwner()
    {
        var thread = new Thread(() =>
        {
            var dialog = new RemoteManager.Protocols.Vnc.VncPasswordDialog("192.168.1.100:5900");
            Assert.NotNull(dialog);
            Assert.Equal(System.Windows.WindowStartupLocation.CenterOwner, dialog.WindowStartupLocation);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void Test_VncHostControl_RefreshDesktop_DoesNotThrow()
    {
        var thread = new Thread(() =>
        {
            var host = new RemoteManager.Protocols.Vnc.VncHostControl();
            Assert.NotNull(host);
            var ex = Record.Exception(() => host.RefreshDesktop());
            Assert.Null(ex);
            host.Dispose();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Theory]
    [InlineData("192.168.1.1 & calc.exe")]
    [InlineData("server.local | whoami")]
    [InlineData("10.0.0.1; notepad.exe")]
    [InlineData("host`dir`")]
    [InlineData("host$test")]
    [InlineData("host\"injection")]
    [InlineData("host'injection")]
    public void Test_SshSessionHandler_RejectsCommandInjectionInHost(string maliciousHost)
    {
        Assert.Throws<ArgumentException>(() =>
            SshSessionHandler.Launch(maliciousHost, 22, "admin"));
    }

    [Theory]
    [InlineData("admin & calc.exe")]
    [InlineData("user; whoami")]
    [InlineData("user with space")]
    [InlineData("user\"quote")]
    public void Test_SshSessionHandler_RejectsCommandInjectionInUsername(string maliciousUser)
    {
        Assert.Throws<ArgumentException>(() =>
            SshSessionHandler.Launch("192.168.1.50", 22, maliciousUser));
    }

    [Theory]
    [InlineData("192.168.1.1 & calc.exe")]
    [InlineData("10.0.0.1; notepad.exe")]
    public void Test_VncSessionHandler_RejectsCommandInjectionInHost(string maliciousHost)
    {
        Assert.Throws<ArgumentException>(() =>
            RemoteManager.Protocols.Vnc.VncSessionHandler.Launch(maliciousHost, 5900));
    }
}

