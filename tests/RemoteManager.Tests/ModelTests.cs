using RemoteManager.Core.Logging;
using RemoteManager.Core.Models;
using Xunit;

namespace RemoteManager.Tests;

public class ModelTests
{
    [Fact]
    public void ConnectionItem_DisplayName_FallsBackToHost_WhenNameIsEmptyOrWhitespace()
    {
        var itemNull = new ConnectionItem { Name = null!, Host = "192.168.1.100" };
        var itemEmpty = new ConnectionItem { Name = "", Host = "192.168.1.100" };
        var itemWhitespace = new ConnectionItem { Name = "   ", Host = "192.168.1.100" };

        Assert.Equal("192.168.1.100", itemNull.DisplayName);
        Assert.Equal("192.168.1.100", itemEmpty.DisplayName);
        Assert.Equal("192.168.1.100", itemWhitespace.DisplayName);
    }

    [Fact]
    public void ConnectionItem_DisplayName_StripsProtocolPrefix_WhenMatchingProtocol()
    {
        var rdpItem = new ConnectionItem
        {
            Name = "RDP: Production Server",
            Host = "10.0.0.1",
            Protocol = ProtocolType.RDP
        };
        var sshItem = new ConnectionItem
        {
            Name = "SSH: bastion.internal",
            Host = "10.0.0.2",
            Protocol = ProtocolType.SSH
        };
        var vncItem = new ConnectionItem
        {
            Name = "VNC: Mac-Mini-Lab",
            Host = "10.0.0.3",
            Protocol = ProtocolType.VNC
        };
        var webItem = new ConnectionItem
        {
            Name = "Web: Router Gateway",
            Host = "10.0.0.4",
            Protocol = ProtocolType.Web
        };

        Assert.Equal("Production Server", rdpItem.DisplayName);
        Assert.Equal("bastion.internal", sshItem.DisplayName);
        Assert.Equal("Mac-Mini-Lab", vncItem.DisplayName);
        Assert.Equal("Router Gateway", webItem.DisplayName);
    }

    [Fact]
    public void ConnectionItem_DisplayName_PreservesName_WhenPrefixDoesNotMatchOrOnlyPrefix()
    {
        // Name without prefix
        var normal = new ConnectionItem
        {
            Name = "Internal DC",
            Host = "10.0.0.1",
            Protocol = ProtocolType.RDP
        };
        Assert.Equal("Internal DC", normal.DisplayName);

        // Name with prefix of another protocol is not stripped
        var mismatched = new ConnectionItem
        {
            Name = "SSH: Wrong Label",
            Host = "10.0.0.2",
            Protocol = ProtocolType.RDP
        };
        Assert.Equal("SSH: Wrong Label", mismatched.DisplayName);

        // Name is only the prefix + spaces (stripped is empty string -> returns raw)
        var onlyPrefix = new ConnectionItem
        {
            Name = "RDP:   ",
            Host = "10.0.0.3",
            Protocol = ProtocolType.RDP
        };
        Assert.Equal("RDP:   ", onlyPrefix.DisplayName);
    }

    [Fact]
    public void ConnectionItem_FullAddress_FormatsWithOrWithoutPort()
    {
        var withPort = new ConnectionItem { Host = "srv.corp.local", Port = 8080 };
        var withZeroPort = new ConnectionItem { Host = "srv.corp.local", Port = 0 };
        var withNegativePort = new ConnectionItem { Host = "srv.corp.local", Port = -1 };

        Assert.Equal("srv.corp.local:8080", withPort.FullAddress);
        Assert.Equal("srv.corp.local", withZeroPort.FullAddress);
        Assert.Equal("srv.corp.local", withNegativePort.FullAddress);
    }

    [Fact]
    public void ConnectionItem_Clone_DeepCopiesAllRelevantFields()
    {
        var originalId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var credentialId = Guid.NewGuid();

        var original = new ConnectionItem
        {
            Id = originalId,
            Name = "Main Database",
            Protocol = ProtocolType.SSH,
            Host = "db.internal",
            Port = 2222,
            GroupId = groupId,
            CredentialId = credentialId,
            DisplayMode = DisplayMode.DetachedWindow,
            SettingsJson = "{\"KeepAlive\": true}"
        };

        var clone = original.Clone();

        Assert.NotEqual(original.Id, clone.Id);
        Assert.Equal("Main Database (Copy)", clone.Name);
        Assert.Equal(ProtocolType.SSH, clone.Protocol);
        Assert.Equal("db.internal", clone.Host);
        Assert.Equal(2222, clone.Port);
        Assert.Equal(groupId, clone.GroupId);
        Assert.Equal(credentialId, clone.CredentialId);
        Assert.Equal(DisplayMode.DetachedWindow, clone.DisplayMode);
        Assert.Equal("{\"KeepAlive\": true}", clone.SettingsJson);
    }

    [Fact]
    public void Credential_ToString_FormatsCorrectlyWithAndWithoutDomain()
    {
        var withDomain = new Credential
        {
            Title = "Admin Key",
            Username = "sysadmin",
            Domain = "CORP"
        };
        var withoutDomain = new Credential
        {
            Title = "Dev SSH",
            Username = "ubuntu",
            Domain = null
        };
        var withWhitespaceDomain = new Credential
        {
            Title = "Dev SSH",
            Username = "ubuntu",
            Domain = "   "
        };

        Assert.Equal(@"Admin Key (CORP\sysadmin)", withDomain.ToString());
        Assert.Equal("Dev SSH (ubuntu)", withoutDomain.ToString());
        Assert.Equal("Dev SSH (ubuntu)", withWhitespaceDomain.ToString());
    }

    [Fact]
    public void AppSettings_DefaultValues_AreConsistent()
    {
        var settings = new AppSettings();

        Assert.Equal(DisplayMode.Tabbed, settings.DefaultDisplayMode);
        Assert.Equal("System", settings.Theme);
        Assert.False(settings.RequireMasterPassword);
        Assert.Null(settings.MasterPasswordHash);
        Assert.Null(settings.MasterPasswordSalt);
        Assert.False(settings.AutoReconnect);
        Assert.True(settings.WarnBeforeDisconnect);
        Assert.Equal("Auto", settings.SshClientType);
        Assert.Null(settings.CustomSshClientPath);
        Assert.Equal("{user}@{host} -p {port}", settings.CustomSshClientArgs);
        Assert.Equal("BuiltIn", settings.VncClientType);
        Assert.Null(settings.CustomVncClientPath);
        Assert.Equal("{host}:{port}", settings.CustomVncClientArgs);

        Assert.True(settings.IsLoggingEnabled);
        Assert.Equal("Debug", settings.MinimumLogLevel);
        Assert.True(settings.LogAppLifecycle);
        Assert.True(settings.LogDatabase);
        Assert.True(settings.LogRdp);
        Assert.True(settings.LogSsh);
        Assert.True(settings.LogVnc);
        Assert.True(settings.LogWeb);
        Assert.True(settings.LogSecurity);
    }

    [Fact]
    public void ConnectionGroup_Initialization_SetsDefaults()
    {
        var group = new ConnectionGroup
        {
            Name = "Cluster A"
        };

        Assert.NotEqual(Guid.Empty, group.Id);
        Assert.Equal("Cluster A", group.Name);
        Assert.Null(group.ParentId);
    }

    [Fact]
    public void LogEntry_PresentationProperties_AndFormatting_AreAccurate()
    {
        var ex = new InvalidOperationException("Test fault");
        var entry = new LogEntry
        {
            Level = LogLevel.Warn,
            Category = "Security",
            Message = "Authentication warning",
            Exception = ex
        };

        Assert.Equal("WARN ", entry.LevelDisplay);
        Assert.Equal("WARNING", entry.LevelBadgeText);
        Assert.Equal("#D83B01", entry.LevelBadgeColor);
        Assert.Equal("#1ED83B01", entry.LevelBadgeBackground);
        Assert.True(entry.HasException);
        Assert.Contains("Test fault", entry.FullExceptionString);

        var logLine = entry.ToFileLogLine();
        Assert.Contains("[WARN] [Security] Authentication warning", logLine);
        Assert.Contains("Exception: ", logLine);
        Assert.Equal(logLine, entry.ToString());
    }

    [Fact]
    public void LogEntry_Levels_BadgeColorsAndLabels_CoverAllLevels()
    {
        var levels = new[] { LogLevel.Trace, LogLevel.Debug, LogLevel.Info, LogLevel.Warn, LogLevel.Error, LogLevel.Fatal };
        foreach (var lvl in levels)
        {
            var e = new LogEntry { Level = lvl };
            Assert.False(string.IsNullOrWhiteSpace(e.LevelDisplay));
            Assert.False(string.IsNullOrWhiteSpace(e.LevelBadgeText));
            Assert.StartsWith("#", e.LevelBadgeColor);
            Assert.StartsWith("#", e.LevelBadgeBackground);
        }
    }

    [Fact]
    public void LogEntry_UndefinedLevel_FallsBackToDefaults()
    {
        var entry = new LogEntry { Level = (LogLevel)999 };
        Assert.Equal("999", entry.LevelDisplay);
        Assert.Equal("999", entry.LevelBadgeText);
        Assert.Equal("#0078D4", entry.LevelBadgeColor);
        Assert.Equal("#1E0078D4", entry.LevelBadgeBackground);
    }

    [Fact]
    public async Task ConnectionCardViewModel_PropertiesAndCatchCoverage()
    {
        var item = new ConnectionItem
        {
            Name = "Card Server",
            Host = "10.0.0.1",
            Port = 22,
            Protocol = (ProtocolType)999,
            DisplayMode = DisplayMode.Tabbed
        };
        var card = new RemoteManager.App.ViewModels.ConnectionCardViewModel(item);

        Assert.Equal(item.Id, card.Id);
        Assert.Equal("Card Server", card.Name);
        Assert.Equal(DisplayMode.Tabbed, card.DisplayMode);
        Assert.Equal(item.CreatedAt, card.CreatedAt);
        Assert.Equal(item.UpdatedAt, card.UpdatedAt);
        Assert.Equal("#0078D4", card.ProtocolBadgeColor);
        Assert.Equal("#200078D4", card.ProtocolBadgeLightBg);

        // Ping with invalid target to exercise catch block in PingAsync
        item.Host = "invalid-hostname-that-fails-lookup-xyz-999.invalid";
        item.Port = -1;
        await card.PingAsync();
        Assert.Equal(RemoteManager.App.ViewModels.ConnectionPingStatus.Offline, card.PingStatus);
    }
}
