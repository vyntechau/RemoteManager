using System.Security.Cryptography;
using RemoteManager.Core.Interfaces;
using RemoteManager.Core.Models;
using RemoteManager.Core.Services;
using RemoteManager.Data;
using RemoteManager.Data.Security;
using Xunit;

namespace RemoteManager.Tests;

public class ExportImportTests : IDisposable
{
    private readonly string _tempDbPath1;
    private readonly string _tempDbPath2;
    private readonly SqliteDatabaseService _db1;
    private readonly SqliteDatabaseService _db2;
    private readonly DpapiEncryptionService _crypto;
    private readonly ExportImportService _service1;
    private readonly ExportImportService _service2;

    public ExportImportTests()
    {
        _tempDbPath1 = Path.Combine(Path.GetTempPath(), $"RemoteManager_ExportTest1_{Guid.NewGuid():N}.db");
        _tempDbPath2 = Path.Combine(Path.GetTempPath(), $"RemoteManager_ExportTest2_{Guid.NewGuid():N}.db");

        _db1 = new SqliteDatabaseService(_tempDbPath1);
        _db2 = new SqliteDatabaseService(_tempDbPath2);
        _crypto = new DpapiEncryptionService();

        _service1 = new ExportImportService(_db1, _crypto);
        _service2 = new ExportImportService(_db2, _crypto);
    }

    public void Dispose()
    {
        try { if (File.Exists(_tempDbPath1)) File.Delete(_tempDbPath1); } catch { }
        try { if (File.Exists(_tempDbPath2)) File.Delete(_tempDbPath2); } catch { }
    }

    [Fact]
    public async Task Test_ExportToJson_And_Import_Roundtrip_Merge()
    {
        // Arrange
        await _db1.InitializeAsync();
        await _db2.InitializeAsync();

        var group = new ConnectionGroup { Name = "Production Servers" };
        await _db1.SaveGroupAsync(group);

        var cred = new Credential
        {
            Title = "Domain Admin",
            Username = "administrator",
            Domain = "CORP",
            EncryptedPassword = _crypto.Encrypt("P@ssw0rd123!")
        };
        await _db1.SaveCredentialAsync(cred);

        var conn = new ConnectionItem
        {
            Name = "Primary DC",
            Host = "192.168.1.10",
            Port = 3389,
            Protocol = ProtocolType.RDP,
            GroupId = group.Id,
            CredentialId = cred.Id,
            DisplayMode = DisplayMode.Fullscreen,
            IsBookmarked = true
        };
        await _db1.SaveConnectionAsync(conn);

        // Act - Export from DB1
        var options = new ExportOptions
        {
            IncludeConnections = true,
            IncludeGroups = true,
            IncludeCredentials = true,
            IncludePasswords = true,
            IncludeSettings = false
        };
        var json = await _service1.ExportToJsonAsync(options);

        Assert.NotNull(json);
        Assert.Contains("Primary DC", json);
        Assert.Contains("Production Servers", json);
        Assert.Contains("administrator", json);

        // Act - Import into DB2
        var importOptions = new ImportOptions
        {
            ConflictResolution = ImportConflictResolution.MergeAndKeepExisting
        };
        var result = await _service2.ImportFromJsonAsync(json, importOptions);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.ConnectionsImported);
        Assert.Equal(1, result.GroupsImported);
        Assert.Equal(1, result.CredentialsImported);

        var db2Conns = await _db2.GetAllConnectionsAsync();
        var db2Groups = await _db2.GetAllGroupsAsync();
        var db2Creds = await _db2.GetAllCredentialsAsync();

        Assert.Single(db2Conns);
        Assert.Equal("Primary DC", db2Conns[0].Name);
        Assert.Equal("192.168.1.10", db2Conns[0].Host);
        Assert.Equal(ProtocolType.RDP, db2Conns[0].Protocol);
        Assert.Equal(DisplayMode.Fullscreen, db2Conns[0].DisplayMode);
        Assert.True(db2Conns[0].IsBookmarked);

        Assert.Single(db2Groups);
        Assert.Equal("Production Servers", db2Groups[0].Name);

        Assert.Single(db2Creds);
        Assert.Equal("Domain Admin", db2Creds[0].Title);
        Assert.Equal("administrator", db2Creds[0].Username);
    }

    [Fact]
    public async Task Test_ExportToJson_With_AesGcm_Passphrase_And_Import_Roundtrip()
    {
        // Arrange
        await _db1.InitializeAsync();
        await _db2.InitializeAsync();

        var plainPassword = "SuperSecretPassword!@#123";
        var cred = new Credential
        {
            Title = "Root SSH",
            Username = "root",
            EncryptedPassword = _crypto.Encrypt(plainPassword)
        };
        await _db1.SaveCredentialAsync(cred);

        var conn = new ConnectionItem
        {
            Name = "Linux Web Server",
            Host = "10.0.0.50",
            Port = 22,
            Protocol = ProtocolType.SSH,
            CredentialId = cred.Id
        };
        await _db1.SaveConnectionAsync(conn);

        var passphrase = "StrongPassphrase456$!";

        // Act - Export with AES-GCM Passphrase
        var exportOptions = new ExportOptions
        {
            IncludeConnections = true,
            IncludeCredentials = true,
            IncludePasswords = true,
            Passphrase = passphrase
        };
        var json = await _service1.ExportToJsonAsync(exportOptions);

        Assert.NotNull(json);
        Assert.Contains("AES-256-GCM-PBKDF2", json);
        Assert.DoesNotContain(plainPassword, json);

        // Act - Import into DB2 with correct passphrase
        var importOptions = new ImportOptions
        {
            Passphrase = passphrase,
            ConflictResolution = ImportConflictResolution.MergeAndKeepExisting
        };
        var result = await _service2.ImportFromJsonAsync(json, importOptions);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.CredentialsImported);

        var importedCreds = await _db2.GetAllCredentialsAsync();
        Assert.Single(importedCreds);
        var decryptedPass = _crypto.Decrypt(importedCreds[0].EncryptedPassword);
        Assert.Equal(plainPassword, decryptedPass);
    }

    [Fact]
    public async Task Test_Import_With_Incorrect_Passphrase_Returns_Error()
    {
        // Arrange
        await _db1.InitializeAsync();
        await _db2.InitializeAsync();

        var cred = new Credential
        {
            Title = "Test Cred",
            Username = "test",
            EncryptedPassword = _crypto.Encrypt("MyPass")
        };
        await _db1.SaveCredentialAsync(cred);

        var exportOptions = new ExportOptions
        {
            IncludeCredentials = true,
            IncludePasswords = true,
            Passphrase = "CorrectPassphrase"
        };
        var json = await _service1.ExportToJsonAsync(exportOptions);

        // Act - Import with wrong passphrase
        var importOptions = new ImportOptions
        {
            Passphrase = "WrongPassphrase",
            ConflictResolution = ImportConflictResolution.MergeAndKeepExisting
        };
        var result = await _service2.ImportFromJsonAsync(json, importOptions);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("incorrect", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Test_Import_ConflictResolution_OverwriteExisting()
    {
        // Arrange
        await _db1.InitializeAsync();
        await _db2.InitializeAsync();

        var connId = Guid.NewGuid();
        var originalConn = new ConnectionItem
        {
            Id = connId,
            Name = "Database Server",
            Host = "192.168.1.5",
            Port = 3306,
            Protocol = ProtocolType.RDP
        };
        await _db2.SaveConnectionAsync(originalConn);

        // Updated in export package
        var updatedConn = new ConnectionItem
        {
            Id = connId,
            Name = "Database Server Updated",
            Host = "192.168.1.5",
            Port = 3307,
            Protocol = ProtocolType.SSH
        };
        await _db1.SaveConnectionAsync(updatedConn);

        var json = await _service1.ExportToJsonAsync(new ExportOptions());

        // Act - Import with OverwriteExisting
        var result = await _service2.ImportFromJsonAsync(json, new ImportOptions
        {
            ConflictResolution = ImportConflictResolution.OverwriteExisting
        });

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.ConnectionsUpdated);

        var db2Conns = await _db2.GetAllConnectionsAsync();
        Assert.Single(db2Conns);
        Assert.Equal("Database Server Updated", db2Conns[0].Name);
        Assert.Equal(3307, db2Conns[0].Port);
        Assert.Equal(ProtocolType.SSH, db2Conns[0].Protocol);
    }

    [Fact]
    public async Task Test_Import_ConflictResolution_CleanAndReplace()
    {
        // Arrange
        await _db1.InitializeAsync();
        await _db2.InitializeAsync();

        // DB2 has old connection
        await _db2.SaveConnectionAsync(new ConnectionItem { Name = "Old Conn", Host = "old.local" });

        // DB1 has new connection
        await _db1.SaveConnectionAsync(new ConnectionItem { Name = "New Conn", Host = "new.local" });

        var json = await _service1.ExportToJsonAsync(new ExportOptions());

        // Act - Import with CleanAndReplace
        var result = await _service2.ImportFromJsonAsync(json, new ImportOptions
        {
            ConflictResolution = ImportConflictResolution.CleanAndReplace
        });

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.ConnectionsImported);

        var db2Conns = await _db2.GetAllConnectionsAsync();
        Assert.Single(db2Conns);
        Assert.Equal("New Conn", db2Conns[0].Name);
    }

    [Fact]
    public async Task Test_ExportToCsv_And_ImportFromCsv_Roundtrip()
    {
        // Arrange
        await _db1.InitializeAsync();
        await _db2.InitializeAsync();

        var group = new ConnectionGroup { Name = "DevOps" };
        await _db1.SaveGroupAsync(group);

        var cred = new Credential { Title = "SSH Key", Username = "ubuntu", Domain = "" };
        await _db1.SaveCredentialAsync(cred);

        var conn1 = new ConnectionItem
        {
            Name = "Kubernetes Master, Node 1",
            Host = "k8s-master.corp",
            Port = 22,
            Protocol = ProtocolType.SSH,
            GroupId = group.Id,
            CredentialId = cred.Id,
            DisplayMode = DisplayMode.Tabbed,
            IsBookmarked = true
        };
        await _db1.SaveConnectionAsync(conn1);

        // Act - Export to CSV
        var csv = await _service1.ExportToCsvAsync();
        Assert.NotNull(csv);
        Assert.Contains("Kubernetes Master, Node 1", csv);
        Assert.Contains("k8s-master.corp", csv);

        // Act - Import into DB2
        var result = await _service2.ImportFromCsvAsync(csv);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.ConnectionsImported);
        Assert.Equal(1, result.GroupsImported);
        Assert.Equal(1, result.CredentialsImported);

        var conns2 = await _db2.GetAllConnectionsAsync();
        Assert.Single(conns2);
        Assert.Equal("Kubernetes Master, Node 1", conns2[0].Name);
        Assert.Equal("k8s-master.corp", conns2[0].Host);
        Assert.Equal(22, conns2[0].Port);
        Assert.Equal(ProtocolType.SSH, conns2[0].Protocol);
        Assert.True(conns2[0].IsBookmarked);
    }

    [Fact]
    public async Task Test_ExportToRdp_And_ImportFromRdp_Roundtrip()
    {
        // Arrange
        await _db1.InitializeAsync();
        await _db2.InitializeAsync();

        var conn = new ConnectionItem
        {
            Name = "Remote Desktop Host",
            Host = "rdp.company.com",
            Port = 3389,
            Protocol = ProtocolType.RDP
        };
        var cred = new Credential
        {
            Title = "User Cred",
            Username = "john.doe",
            Domain = "COMPANY"
        };

        // Act - Export to RDP
        var rdpString = await _service1.ExportToRdpAsync(conn, cred);

        Assert.NotNull(rdpString);
        Assert.Contains("full address:s:rdp.company.com:3389", rdpString);
        Assert.Contains("username:s:john.doe", rdpString);
        Assert.Contains("domain:s:COMPANY", rdpString);

        // Act - Import from RDP string
        var importedConn = await _service2.ImportFromRdpAsync(rdpString, "My Imported RDP");

        // Assert
        Assert.NotNull(importedConn);
        Assert.Equal("My Imported RDP", importedConn.Name);
        Assert.Equal("rdp.company.com", importedConn.Host);
        Assert.Equal(3389, importedConn.Port);
        Assert.Equal(ProtocolType.RDP, importedConn.Protocol);
        Assert.NotNull(importedConn.CredentialId);

        var creds = await _db2.GetAllCredentialsAsync();
        Assert.Single(creds);
        Assert.Equal("john.doe", creds[0].Username);
        Assert.Equal("COMPANY", creds[0].Domain);
    }

    [Fact]
    public async Task Test_Import_Invalid_Json_Returns_Error()
    {
        var result = await _service1.ImportFromJsonAsync("invalid json here", new ImportOptions());
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void Test_ExportDialog_Initialization_NoNullReferenceException()
    {
        StaTestHelper.Run(() =>
        {
            var dialog = new RemoteManager.App.Views.ExportDialog(_service1);
            Assert.NotNull(dialog);
            Assert.False(dialog.WasExportSuccessful);
        });
    }

    [Fact]
    public void Test_ImportDialog_Initialization_NoNullReferenceException()
    {
        StaTestHelper.Run(() =>
        {
            var dialog = new RemoteManager.App.Views.ImportDialog(_service1);
            Assert.NotNull(dialog);
            Assert.False(dialog.WasImportSuccessful);
        });
    }
}
