using RemoteManager.Core.Models;
using RemoteManager.Data;
using RemoteManager.Data.Security;
using Xunit;

namespace RemoteManager.Tests;

public class DatabaseAndSecurityTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteDatabaseService _db;
    private readonly DpapiEncryptionService _crypto;

    public DatabaseAndSecurityTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"RemoteManager_Test_{Guid.NewGuid():N}.db");
        _db = new SqliteDatabaseService(_tempDbPath);
        _crypto = new DpapiEncryptionService();
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_tempDbPath))
            {
                File.Delete(_tempDbPath);
            }
        }
        catch { }
    }

    [Fact]
    public void Test_DpapiEncryption_Roundtrip()
    {
        // Arrange
        var originalSecret = "SuperSecurePassword123!@#$";

        // Act
        var cipherText = _crypto.Encrypt(originalSecret);
        var decrypted = _crypto.Decrypt(cipherText);

        // Assert
        Assert.NotNull(cipherText);
        Assert.NotEmpty(cipherText);
        Assert.NotEqual(originalSecret, cipherText);
        Assert.Equal(originalSecret, decrypted);
    }

    [Fact]
    public async Task Test_Database_Initialization_And_Connection_Crud()
    {
        // Arrange & Act (Init)
        await _db.InitializeAsync();

        var connection = new ConnectionItem
        {
            Name = "Primary DB Server",
            Host = "10.0.0.1",
            Port = 3389,
            Protocol = ProtocolType.RDP,
            DisplayMode = DisplayMode.Tabbed
        };

        // Insert
        await _db.SaveConnectionAsync(connection);

        // Query
        var retrieved = await _db.GetConnectionByIdAsync(connection.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("Primary DB Server", retrieved.Name);
        Assert.Equal("10.0.0.1", retrieved.Host);
        Assert.Equal(3389, retrieved.Port);

        // Update
        retrieved.Name = "Updated DB Server";
        await _db.SaveConnectionAsync(retrieved);

        var updated = await _db.GetConnectionByIdAsync(connection.Id);
        Assert.NotNull(updated);
        Assert.Equal("Updated DB Server", updated.Name);

        // Delete
        await _db.DeleteConnectionAsync(connection.Id);
        var deleted = await _db.GetConnectionByIdAsync(connection.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task Test_MultipleAccounts_On_Same_IP_Isolation()
    {
        // Arrange
        await _db.InitializeAsync();

        const string serverIp = "192.168.1.50";

        // Account 1: Alice
        var credAlice = new Credential
        {
            Title = "Alice RDP Login",
            Username = "alice",
            Domain = "CORP",
            EncryptedPassword = _crypto.Encrypt("AlicePassword_987#")
        };
        await _db.SaveCredentialAsync(credAlice);

        var connAlice = new ConnectionItem
        {
            Name = "Prod Server - Alice",
            Host = serverIp,
            Port = 3389,
            Protocol = ProtocolType.RDP,
            CredentialId = credAlice.Id
        };
        await _db.SaveConnectionAsync(connAlice);

        // Account 2: Bob (SAME IP!)
        var credBob = new Credential
        {
            Title = "Bob RDP Login",
            Username = "bob",
            Domain = "CORP",
            EncryptedPassword = _crypto.Encrypt("BobPassword_123$")
        };
        await _db.SaveCredentialAsync(credBob);

        var connBob = new ConnectionItem
        {
            Name = "Prod Server - Bob",
            Host = serverIp,
            Port = 3389,
            Protocol = ProtocolType.RDP,
            CredentialId = credBob.Id
        };
        await _db.SaveConnectionAsync(connBob);

        // Act: Query all connections and credentials
        var allConnections = await _db.GetAllConnectionsAsync();
        var sameIpConnections = allConnections.Where(c => c.Host == serverIp).ToList();

        // Assert: Both connections exist independently on the same IP
        Assert.Equal(2, sameIpConnections.Count);

        var aliceFetchedConn = sameIpConnections.First(c => c.Name.Contains("Alice"));
        var bobFetchedConn = sameIpConnections.First(c => c.Name.Contains("Bob"));

        Assert.NotEqual(aliceFetchedConn.CredentialId, bobFetchedConn.CredentialId);

        // Verify credentials can be independently decrypted without collision
        var aliceFetchedCred = await _db.GetCredentialByIdAsync(aliceFetchedConn.CredentialId!.Value);
        var bobFetchedCred = await _db.GetCredentialByIdAsync(bobFetchedConn.CredentialId!.Value);

        Assert.NotNull(aliceFetchedCred);
        Assert.NotNull(bobFetchedCred);

        Assert.Equal("alice", aliceFetchedCred.Username);
        Assert.Equal("AlicePassword_987#", _crypto.Decrypt(aliceFetchedCred.EncryptedPassword));

        Assert.Equal("bob", bobFetchedCred.Username);
        Assert.Equal("BobPassword_123$", _crypto.Decrypt(bobFetchedCred.EncryptedPassword));
    }

    [Fact]
    public void Test_DpapiEncryption_EdgeCases_NullEmptyAndCorrupt()
    {
        // Null or empty plainText returns empty string
        Assert.Equal(string.Empty, _crypto.Encrypt(string.Empty));
        Assert.Equal(string.Empty, _crypto.Encrypt(null!));

        // Null or empty cipherText returns empty string
        Assert.Equal(string.Empty, _crypto.Decrypt(string.Empty));
        Assert.Equal(string.Empty, _crypto.Decrypt(null!));

        // Corrupted base64 or invalid encrypted data throws CryptographicException
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() =>
        {
            _crypto.Decrypt("NotValidBase64@@@");
        });

        Assert.Throws<System.Security.Cryptography.CryptographicException>(() =>
        {
            // Valid base64 but invalid cipher bytes
            _crypto.Decrypt(Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        });
    }

    [Fact]
    public async Task Test_Database_Credentials_Crud_And_EmptyLookups()
    {
        await _db.InitializeAsync();

        // Non-existent ID returns null
        var nonExistentCred = await _db.GetCredentialByIdAsync(Guid.NewGuid());
        Assert.Null(nonExistentCred);

        var nonExistentConn = await _db.GetConnectionByIdAsync(Guid.NewGuid());
        Assert.Null(nonExistentConn);

        // Create Credentials
        var cred1 = new Credential
        {
            Title = "Zebra Login",
            Username = "zebra_user",
            Domain = "CORP",
            EncryptedPassword = _crypto.Encrypt("ZebraPass123"),
            Notes = "Zebra notes"
        };
        var cred2 = new Credential
        {
            Title = "Alpha Login",
            Username = "alpha_user",
            Domain = null,
            EncryptedPassword = _crypto.Encrypt("AlphaPass123"),
            Notes = null
        };

        await _db.SaveCredentialAsync(cred1);
        await _db.SaveCredentialAsync(cred2);

        // GetAllCredentialsAsync should be ordered by Title ASC ("Alpha Login" before "Zebra Login")
        var allCreds = await _db.GetAllCredentialsAsync();
        Assert.True(allCreds.Count >= 2);
        var alphaIdx = allCreds.FindIndex(c => c.Id == cred2.Id);
        var zebraIdx = allCreds.FindIndex(c => c.Id == cred1.Id);
        Assert.True(alphaIdx >= 0 && zebraIdx >= 0);
        Assert.True(alphaIdx < zebraIdx, "Alpha Login should precede Zebra Login");

        // Update Credential
        cred2.Title = "Alpha Login Updated";
        cred2.Notes = "Added note";
        await _db.SaveCredentialAsync(cred2);

        var updatedCred2 = await _db.GetCredentialByIdAsync(cred2.Id);
        Assert.NotNull(updatedCred2);
        Assert.Equal("Alpha Login Updated", updatedCred2.Title);
        Assert.Equal("Added note", updatedCred2.Notes);

        // Delete Credential
        await _db.DeleteCredentialAsync(cred1.Id);
        var deletedCred = await _db.GetCredentialByIdAsync(cred1.Id);
        Assert.Null(deletedCred);
    }

    [Fact]
    public async Task Test_Database_Groups_Crud_And_ParentHierarchy()
    {
        await _db.InitializeAsync();

        // Initial groups should be empty or contain standard items
        var initialGroups = await _db.GetAllGroupsAsync();
        var initialCount = initialGroups.Count;

        // Create root group and child group
        var rootGroup = new ConnectionGroup
        {
            Name = "Production Servers",
            ParentId = null
        };
        await _db.SaveGroupAsync(rootGroup);

        var childGroup = new ConnectionGroup
        {
            Name = "Web Tier",
            ParentId = rootGroup.Id
        };
        await _db.SaveGroupAsync(childGroup);

        var groups = await _db.GetAllGroupsAsync();
        Assert.Equal(initialCount + 2, groups.Count);

        var retrievedRoot = groups.FirstOrDefault(g => g.Id == rootGroup.Id);
        var retrievedChild = groups.FirstOrDefault(g => g.Id == childGroup.Id);

        Assert.NotNull(retrievedRoot);
        Assert.NotNull(retrievedChild);
        Assert.Equal("Production Servers", retrievedRoot.Name);
        Assert.Null(retrievedRoot.ParentId);
        Assert.Equal("Web Tier", retrievedChild.Name);
        Assert.Equal(rootGroup.Id, retrievedChild.ParentId);

        // Update group name
        retrievedChild.Name = "Web Tier Updated";
        await _db.SaveGroupAsync(retrievedChild);

        var updatedGroups = await _db.GetAllGroupsAsync();
        var updatedChild = updatedGroups.First(g => g.Id == childGroup.Id);
        Assert.Equal("Web Tier Updated", updatedChild.Name);

        // Delete child group
        await _db.DeleteGroupAsync(childGroup.Id);
        var remainingGroups = await _db.GetAllGroupsAsync();
        Assert.DoesNotContain(remainingGroups, g => g.Id == childGroup.Id);
        Assert.Contains(remainingGroups, g => g.Id == rootGroup.Id);

        // Clean up root
        await _db.DeleteGroupAsync(rootGroup.Id);
    }

    [Fact]
    public async Task Test_Database_Settings_Persistence_And_CorruptJsonFallback()
    {
        await _db.InitializeAsync();

        // Getting settings before any save should return defaults
        var defaultSettings = await _db.GetSettingsAsync();
        Assert.NotNull(defaultSettings);
        Assert.True(defaultSettings.IsLoggingEnabled);
        Assert.Equal("System", defaultSettings.Theme);

        // Modify and save settings
        var customSettings = new AppSettings
        {
            Theme = "Dark",
            IsLoggingEnabled = false,
            MinimumLogLevel = "Error",
            AutoReconnect = true,
            RequireMasterPassword = true,
            SshClientType = "PuTTY",
            CustomSshClientPath = @"C:\putty.exe"
        };
        await _db.SaveSettingsAsync(customSettings);

        var reloaded = await _db.GetSettingsAsync();
        Assert.NotNull(reloaded);
        Assert.Equal("Dark", reloaded.Theme);
        Assert.False(reloaded.IsLoggingEnabled);
        Assert.Equal("Error", reloaded.MinimumLogLevel);
        Assert.True(reloaded.AutoReconnect);
        Assert.True(reloaded.RequireMasterPassword);
        Assert.Equal("PuTTY", reloaded.SshClientType);
        Assert.Equal(@"C:\putty.exe", reloaded.CustomSshClientPath);

        // Simulate corrupt JSON in database
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = _tempDbPath }.ToString()))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Settings SET Value = '{ INVALID JSON DATA }' WHERE Key = 'app_settings';";
            await cmd.ExecuteNonQueryAsync();
        }

        // GetSettingsAsync should gracefully fall back to default settings without throwing
        var fallbackSettings = await _db.GetSettingsAsync();
        Assert.NotNull(fallbackSettings);
        Assert.True(fallbackSettings.IsLoggingEnabled);
        Assert.Equal("System", fallbackSettings.Theme);
    }

    [Fact]
    public async Task Test_Database_ErrorPaths_CatchBlockCoverage()
    {
        // Target an invalid path that cannot be accessed or written to
        var invalidDb = new SqliteDatabaseService(@"Z:\NonExistentDriveXYZ\Invalid\db.sqlite");

        await Assert.ThrowsAnyAsync<Exception>(() => invalidDb.InitializeAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => invalidDb.GetAllConnectionsAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => invalidDb.GetConnectionByIdAsync(Guid.NewGuid()));
        await Assert.ThrowsAnyAsync<Exception>(() => invalidDb.SaveConnectionAsync(new ConnectionItem()));
        await Assert.ThrowsAnyAsync<Exception>(() => invalidDb.DeleteConnectionAsync(Guid.NewGuid()));

        await Assert.ThrowsAnyAsync<Exception>(() => invalidDb.GetAllCredentialsAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => invalidDb.GetCredentialByIdAsync(Guid.NewGuid()));
        await Assert.ThrowsAnyAsync<Exception>(() => invalidDb.SaveCredentialAsync(new Credential()));
        await Assert.ThrowsAnyAsync<Exception>(() => invalidDb.DeleteCredentialAsync(Guid.NewGuid()));

        await Assert.ThrowsAnyAsync<Exception>(() => invalidDb.GetAllGroupsAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => invalidDb.SaveGroupAsync(new ConnectionGroup()));
        await Assert.ThrowsAnyAsync<Exception>(() => invalidDb.DeleteGroupAsync(Guid.NewGuid()));

        await Assert.ThrowsAnyAsync<Exception>(() => invalidDb.GetSettingsAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => invalidDb.SaveSettingsAsync(new AppSettings()));
    }

    [Fact]
    public void Test_Database_DefaultConstructor_InitializesPath()
    {
        // Default constructor uses AppData
        var defaultDb = new SqliteDatabaseService();
        Assert.NotNull(defaultDb);
    }

    [Fact]
    public async Task Test_Database_DeleteConnection_CleansUpWebView2ProfileDirectory()
    {
        await _db.InitializeAsync();
        var conn = new ConnectionItem { Name = "Web Target", Host = "10.0.0.1", Protocol = ProtocolType.Web };
        await _db.SaveConnectionAsync(conn);

        // Pre-create WebView2 profile folder
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profileDir = Path.Combine(appData, "RemoteManager", "WebProfiles", conn.Id.ToString("N"));
        Directory.CreateDirectory(profileDir);
        Assert.True(Directory.Exists(profileDir));

        await _db.DeleteConnectionAsync(conn.Id);
        Assert.False(Directory.Exists(profileDir));
    }

    [Fact]
    public async Task Test_Database_GetSettingsAsync_CorruptedJson_FallsBackToDefault()
    {
        await _db.InitializeAsync();

        // Write malformed JSON directly to Settings table
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_tempDbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO Settings (Key, Value) VALUES ('app_settings', '{ malformed json: not valid }');";
            await cmd.ExecuteNonQueryAsync();
        }

        var loaded = await _db.GetSettingsAsync();
        Assert.NotNull(loaded);
        Assert.Equal(DisplayMode.Tabbed, loaded.DefaultDisplayMode);

        // Test with "null" string
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_tempDbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO Settings (Key, Value) VALUES ('app_settings', 'null');";
            await cmd.ExecuteNonQueryAsync();
        }

        var nullResult = await _db.GetSettingsAsync();
        Assert.NotNull(nullResult);
    }

    [Fact]
    public async Task Test_Database_DeleteConnection_ProfileDirectoryCleanup_FailsGracefully()
    {
        await _db.InitializeAsync();
        var conn = new ConnectionItem { Name = "Web Target Locked", Host = "10.0.0.1", Protocol = ProtocolType.Web };
        await _db.SaveConnectionAsync(conn);

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profileDir = Path.Combine(appData, "RemoteManager", "WebProfiles", conn.Id.ToString("N"));
        Directory.CreateDirectory(profileDir);
        var lockedFilePath = Path.Combine(profileDir, "locked.bin");

        // Lock a file inside the profile directory
        using (var fs = new FileStream(lockedFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            // Deleting connection should catch the IOException during profile cleanup and not throw
            await _db.DeleteConnectionAsync(conn.Id);
        }

        // Clean up afterwards
        if (Directory.Exists(profileDir))
        {
            Directory.Delete(profileDir, recursive: true);
        }
    }

    [Fact]
    public void Test_DpapiEncryptionService_ProtectAndUnprotect_ExceptionPaths()
    {
        var crypto = new DpapiEncryptionService();

        // 1. ProtectFunc throws
        crypto.ProtectFunc = (bytes, entropy, scope) => throw new InvalidOperationException("Simulated protect failure");
        var exProtect = Assert.Throws<System.Security.Cryptography.CryptographicException>(() => crypto.Encrypt("SampleSecret"));
        Assert.Contains("Failed to encrypt data using DPAPI", exProtect.Message);

        // 2. UnprotectFunc throws
        var validCipher = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });
        crypto.UnprotectFunc = (bytes, entropy, scope) => throw new InvalidOperationException("Simulated unprotect failure");
        var exUnprotect = Assert.Throws<System.Security.Cryptography.CryptographicException>(() => crypto.Decrypt(validCipher));
        Assert.Contains("Failed to decrypt data using DPAPI", exUnprotect.Message);
    }

    [Fact]
    public async Task Test_Connection_IsBookmarked_And_SortOrder_Persistence()
    {
        await _db.InitializeAsync();

        var item = new ConnectionItem
        {
            Name = "Special Bookmarked Server",
            Host = "192.168.10.50",
            Port = 22,
            Protocol = ProtocolType.SSH,
            IsBookmarked = true,
            SortOrder = 7
        };

        await _db.SaveConnectionAsync(item);

        var retrieved = await _db.GetConnectionByIdAsync(item.Id);
        Assert.NotNull(retrieved);
        Assert.True(retrieved.IsBookmarked);
        Assert.Equal(7, retrieved.SortOrder);

        // Update to unbookmarked
        retrieved.IsBookmarked = false;
        retrieved.SortOrder = 1;
        await _db.SaveConnectionAsync(retrieved);

        var updated = await _db.GetConnectionByIdAsync(item.Id);
        Assert.NotNull(updated);
        Assert.False(updated.IsBookmarked);
        Assert.Equal(1, updated.SortOrder);
    }

    [Fact]
    public async Task Test_UpdateConnectionsOrderAsync_UpdatesAllOrders()
    {
        await _db.InitializeAsync();

        var c1 = new ConnectionItem { Name = "Server A", Host = "10.0.0.1", IsBookmarked = false, SortOrder = 0 };
        var c2 = new ConnectionItem { Name = "Server B", Host = "10.0.0.2", IsBookmarked = true, SortOrder = 1 };
        var c3 = new ConnectionItem { Name = "Server C", Host = "10.0.0.3", IsBookmarked = true, SortOrder = 2 };

        await _db.SaveConnectionAsync(c1);
        await _db.SaveConnectionAsync(c2);
        await _db.SaveConnectionAsync(c3);

        // Reorder list: c2 (order 0), c3 (order 1), c1 (order 2)
        var reordered = new List<ConnectionItem> { c2, c3, c1 };
        await _db.UpdateConnectionsOrderAsync(reordered);

        var all = await _db.GetAllConnectionsAsync();
        // GetAllConnectionsAsync returns ORDER BY IsBookmarked DESC, SortOrder ASC
        Assert.Equal(3, all.Count);
        Assert.Equal(c2.Id, all[0].Id);
        Assert.Equal(0, all[0].SortOrder);
        Assert.True(all[0].IsBookmarked);

        Assert.Equal(c3.Id, all[1].Id);
        Assert.Equal(1, all[1].SortOrder);
        Assert.True(all[1].IsBookmarked);

        Assert.Equal(c1.Id, all[2].Id);
        Assert.Equal(2, all[2].SortOrder);
        Assert.False(all[2].IsBookmarked);
    }
}



