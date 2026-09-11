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
}
