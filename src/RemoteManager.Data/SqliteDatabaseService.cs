using System.Text.Json;
using Microsoft.Data.Sqlite;
using RemoteManager.Core.Interfaces;
using RemoteManager.Core.Logging;
using RemoteManager.Core.Models;

namespace RemoteManager.Data;

public class SqliteDatabaseService : IDatabaseService
{
    private readonly string _connectionString;
    private readonly string _dbPath;

    public SqliteDatabaseService(string? customDbPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customDbPath))
        {
            _dbPath = customDbPath;
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var directory = Path.Combine(appData, "RemoteManager");
            Directory.CreateDirectory(directory);
            _dbPath = Path.Combine(directory, "remotemanager.db");
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        LogEngine.Instance.Debug("Database", $"SQLite database initialized with target path: {_dbPath}");
    }

    private SqliteConnection CreateConnection() => new(_connectionString);

    public async Task InitializeAsync()
    {
        LogEngine.Instance.Info("Database", "Initializing database tables and schema...");
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();

            var sql = @"
                CREATE TABLE IF NOT EXISTS Groups (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    ParentId TEXT
                );

                CREATE TABLE IF NOT EXISTS Credentials (
                    Id TEXT PRIMARY KEY,
                    Title TEXT NOT NULL,
                    Username TEXT NOT NULL,
                    Domain TEXT,
                    EncryptedPassword TEXT NOT NULL,
                    Notes TEXT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS Connections (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Protocol INTEGER NOT NULL,
                    Host TEXT NOT NULL,
                    Port INTEGER NOT NULL,
                    GroupId TEXT,
                    CredentialId TEXT,
                    DisplayMode INTEGER NOT NULL,
                    SettingsJson TEXT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY (GroupId) REFERENCES Groups(Id) ON DELETE SET NULL,
                    FOREIGN KEY (CredentialId) REFERENCES Credentials(Id) ON DELETE SET NULL
                );

                CREATE TABLE IF NOT EXISTS Settings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL
                );
            ";

            using var cmd = new SqliteCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
            LogEngine.Instance.Info("Database", "Database schema initialized successfully.");
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("Database", "Failed to initialize database schema", ex);
            throw;
        }
    }

    #region Connections
    public async Task<List<ConnectionItem>> GetAllConnectionsAsync()
    {
        LogEngine.Instance.Debug("Database", "Loading all connections from database...");
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();

            var sql = "SELECT Id, Name, Protocol, Host, Port, GroupId, CredentialId, DisplayMode, SettingsJson, CreatedAt, UpdatedAt FROM Connections ORDER BY Name ASC;";
            using var cmd = new SqliteCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            var list = new List<ConnectionItem>();
            while (await reader.ReadAsync())
            {
                list.Add(new ConnectionItem
                {
                    Id = Guid.Parse(reader.GetString(0)),
                    Name = reader.GetString(1),
                    Protocol = (ProtocolType)reader.GetInt32(2),
                    Host = reader.GetString(3),
                    Port = reader.GetInt32(4),
                    GroupId = reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)),
                    CredentialId = reader.IsDBNull(6) ? null : Guid.Parse(reader.GetString(6)),
                    DisplayMode = (DisplayMode)reader.GetInt32(7),
                    SettingsJson = reader.IsDBNull(8) ? null : reader.GetString(8),
                    CreatedAt = DateTime.Parse(reader.GetString(9)),
                    UpdatedAt = DateTime.Parse(reader.GetString(10))
                });
            }
            LogEngine.Instance.Debug("Database", $"Retrieved {list.Count} connections.");
            return list;
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("Database", "Error loading connections", ex);
            throw;
        }
    }

    public async Task<ConnectionItem?> GetConnectionByIdAsync(Guid id)
    {
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();

            var sql = "SELECT Id, Name, Protocol, Host, Port, GroupId, CredentialId, DisplayMode, SettingsJson, CreatedAt, UpdatedAt FROM Connections WHERE Id = @Id;";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id.ToString());

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new ConnectionItem
                {
                    Id = Guid.Parse(reader.GetString(0)),
                    Name = reader.GetString(1),
                    Protocol = (ProtocolType)reader.GetInt32(2),
                    Host = reader.GetString(3),
                    Port = reader.GetInt32(4),
                    GroupId = reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)),
                    CredentialId = reader.IsDBNull(6) ? null : Guid.Parse(reader.GetString(6)),
                    DisplayMode = (DisplayMode)reader.GetInt32(7),
                    SettingsJson = reader.IsDBNull(8) ? null : reader.GetString(8),
                    CreatedAt = DateTime.Parse(reader.GetString(9)),
                    UpdatedAt = DateTime.Parse(reader.GetString(10))
                };
            }
            return null;
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("Database", $"Error fetching connection with Id {id}", ex);
            throw;
        }
    }

    public async Task SaveConnectionAsync(ConnectionItem connection)
    {
        LogEngine.Instance.Info("Database", $"Saving connection '{connection.Name}' (ID: {connection.Id}, Protocol: {connection.Protocol}, Host: {connection.Host}:{connection.Port})");
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();

            var sql = @"
                INSERT INTO Connections (Id, Name, Protocol, Host, Port, GroupId, CredentialId, DisplayMode, SettingsJson, CreatedAt, UpdatedAt)
                VALUES (@Id, @Name, @Protocol, @Host, @Port, @GroupId, @CredentialId, @DisplayMode, @SettingsJson, @CreatedAt, @UpdatedAt)
                ON CONFLICT(Id) DO UPDATE SET
                    Name = excluded.Name,
                    Protocol = excluded.Protocol,
                    Host = excluded.Host,
                    Port = excluded.Port,
                    GroupId = excluded.GroupId,
                    CredentialId = excluded.CredentialId,
                    DisplayMode = excluded.DisplayMode,
                    SettingsJson = excluded.SettingsJson,
                    UpdatedAt = excluded.UpdatedAt;
            ";

            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", connection.Id.ToString());
            cmd.Parameters.AddWithValue("@Name", connection.Name);
            cmd.Parameters.AddWithValue("@Protocol", (int)connection.Protocol);
            cmd.Parameters.AddWithValue("@Host", connection.Host);
            cmd.Parameters.AddWithValue("@Port", connection.Port);
            cmd.Parameters.AddWithValue("@GroupId", (object?)connection.GroupId?.ToString() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CredentialId", (object?)connection.CredentialId?.ToString() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DisplayMode", (int)connection.DisplayMode);
            cmd.Parameters.AddWithValue("@SettingsJson", (object?)connection.SettingsJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedAt", connection.CreatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("O"));

            await cmd.ExecuteNonQueryAsync();
            LogEngine.Instance.Debug("Database", $"Connection '{connection.Name}' saved successfully.");
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("Database", $"Failed to save connection '{connection.Name}'", ex);
            throw;
        }
    }

    public async Task DeleteConnectionAsync(Guid id)
    {
        LogEngine.Instance.Info("Database", $"Deleting connection with Id {id}");
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();

            var sql = "DELETE FROM Connections WHERE Id = @Id;";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id.ToString());
            await cmd.ExecuteNonQueryAsync();
            LogEngine.Instance.Debug("Database", $"Connection {id} deleted.");

            // Clean up isolated WebView2 profile cache for this connection if it exists
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var profileDir = Path.Combine(appData, "RemoteManager", "WebProfiles", id.ToString("N"));
                if (Directory.Exists(profileDir))
                {
                    Directory.Delete(profileDir, recursive: true);
                    LogEngine.Instance.Debug("Database", $"Cleaned up WebView2 profile cache for connection {id}.");
                }
            }
            catch (Exception profileEx)
            {
                LogEngine.Instance.Warn("Database", $"Could not clean up WebView2 profile directory for {id}: {profileEx.Message}");
            }
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("Database", $"Failed to delete connection {id}", ex);
            throw;
        }
    }
    #endregion

    #region Credentials
    public async Task<List<Credential>> GetAllCredentialsAsync()
    {
        LogEngine.Instance.Debug("Database", "Loading all credentials from database...");
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();

            var sql = "SELECT Id, Title, Username, Domain, EncryptedPassword, Notes, CreatedAt, UpdatedAt FROM Credentials ORDER BY Title ASC;";
            using var cmd = new SqliteCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            var list = new List<Credential>();
            while (await reader.ReadAsync())
            {
                list.Add(new Credential
                {
                    Id = Guid.Parse(reader.GetString(0)),
                    Title = reader.GetString(1),
                    Username = reader.GetString(2),
                    Domain = reader.IsDBNull(3) ? null : reader.GetString(3),
                    EncryptedPassword = reader.GetString(4),
                    Notes = reader.IsDBNull(5) ? null : reader.GetString(5),
                    CreatedAt = DateTime.Parse(reader.GetString(6)),
                    UpdatedAt = DateTime.Parse(reader.GetString(7))
                });
            }
            LogEngine.Instance.Debug("Database", $"Retrieved {list.Count} credentials.");
            return list;
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("Database", "Error loading credentials", ex);
            throw;
        }
    }

    public async Task<Credential?> GetCredentialByIdAsync(Guid id)
    {
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();

            var sql = "SELECT Id, Title, Username, Domain, EncryptedPassword, Notes, CreatedAt, UpdatedAt FROM Credentials WHERE Id = @Id;";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id.ToString());

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new Credential
                {
                    Id = Guid.Parse(reader.GetString(0)),
                    Title = reader.GetString(1),
                    Username = reader.GetString(2),
                    Domain = reader.IsDBNull(3) ? null : reader.GetString(3),
                    EncryptedPassword = reader.GetString(4),
                    Notes = reader.IsDBNull(5) ? null : reader.GetString(5),
                    CreatedAt = DateTime.Parse(reader.GetString(6)),
                    UpdatedAt = DateTime.Parse(reader.GetString(7))
                };
            }
            return null;
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("Database", $"Error fetching credential {id}", ex);
            throw;
        }
    }

    public async Task SaveCredentialAsync(Credential credential)
    {
        LogEngine.Instance.Info("Database", $"Saving credential '{credential.Title}' (Username: '{credential.Username}', Domain: '{credential.Domain}')");
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();

            var sql = @"
                INSERT INTO Credentials (Id, Title, Username, Domain, EncryptedPassword, Notes, CreatedAt, UpdatedAt)
                VALUES (@Id, @Title, @Username, @Domain, @EncryptedPassword, @Notes, @CreatedAt, @UpdatedAt)
                ON CONFLICT(Id) DO UPDATE SET
                    Title = excluded.Title,
                    Username = excluded.Username,
                    Domain = excluded.Domain,
                    EncryptedPassword = excluded.EncryptedPassword,
                    Notes = excluded.Notes,
                    UpdatedAt = excluded.UpdatedAt;
            ";

            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", credential.Id.ToString());
            cmd.Parameters.AddWithValue("@Title", credential.Title);
            cmd.Parameters.AddWithValue("@Username", credential.Username);
            cmd.Parameters.AddWithValue("@Domain", (object?)credential.Domain ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@EncryptedPassword", credential.EncryptedPassword);
            cmd.Parameters.AddWithValue("@Notes", (object?)credential.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedAt", credential.CreatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("O"));

            await cmd.ExecuteNonQueryAsync();
            LogEngine.Instance.Debug("Database", $"Credential '{credential.Title}' saved.");
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("Database", $"Failed to save credential '{credential.Title}'", ex);
            throw;
        }
    }

    public async Task DeleteCredentialAsync(Guid id)
    {
        LogEngine.Instance.Info("Database", $"Deleting credential with Id {id}");
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();

            var sql = "DELETE FROM Credentials WHERE Id = @Id;";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id.ToString());
            await cmd.ExecuteNonQueryAsync();
            LogEngine.Instance.Debug("Database", $"Credential {id} deleted.");
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("Database", $"Failed to delete credential {id}", ex);
            throw;
        }
    }
    #endregion

    #region Groups
    public async Task<List<ConnectionGroup>> GetAllGroupsAsync()
    {
        LogEngine.Instance.Debug("Database", "Loading all groups from database...");
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();

            var sql = "SELECT Id, Name, ParentId FROM Groups ORDER BY Name ASC;";
            using var cmd = new SqliteCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            var list = new List<ConnectionGroup>();
            while (await reader.ReadAsync())
            {
                list.Add(new ConnectionGroup
                {
                    Id = Guid.Parse(reader.GetString(0)),
                    Name = reader.GetString(1),
                    ParentId = reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2))
                });
            }
            LogEngine.Instance.Debug("Database", $"Retrieved {list.Count} groups.");
            return list;
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("Database", "Error loading groups", ex);
            throw;
        }
    }

    public async Task SaveGroupAsync(ConnectionGroup group)
    {
        LogEngine.Instance.Info("Database", $"Saving group '{group.Name}' (ID: {group.Id}, ParentId: {group.ParentId})");
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();

            var sql = @"
                INSERT INTO Groups (Id, Name, ParentId)
                VALUES (@Id, @Name, @ParentId)
                ON CONFLICT(Id) DO UPDATE SET
                    Name = excluded.Name,
                    ParentId = excluded.ParentId;
            ";

            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", group.Id.ToString());
            cmd.Parameters.AddWithValue("@Name", group.Name);
            cmd.Parameters.AddWithValue("@ParentId", (object?)group.ParentId?.ToString() ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
            LogEngine.Instance.Debug("Database", $"Group '{group.Name}' saved.");
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("Database", $"Failed to save group '{group.Name}'", ex);
            throw;
        }
    }

    public async Task DeleteGroupAsync(Guid id)
    {
        LogEngine.Instance.Info("Database", $"Deleting group with Id {id}");
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();

            var sql = "DELETE FROM Groups WHERE Id = @Id;";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id.ToString());
            await cmd.ExecuteNonQueryAsync();
            LogEngine.Instance.Debug("Database", $"Group {id} deleted.");
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("Database", $"Failed to delete group {id}", ex);
            throw;
        }
    }
    #endregion

    #region Settings
    public async Task<AppSettings> GetSettingsAsync()
    {
        LogEngine.Instance.Debug("Database", "Loading application settings...");
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();

            var sql = "SELECT Value FROM Settings WHERE Key = 'app_settings';";
            using var cmd = new SqliteCommand(sql, conn);
            var result = await cmd.ExecuteScalarAsync();

            if (result != null && result is string json)
            {
                try
                {
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        LogEngine.Instance.Debug("Database", "Application settings loaded successfully.");
                        return settings;
                    }
                }
                catch (Exception ex)
                {
                    LogEngine.Instance.Warn("Database", "Failed to deserialize settings json, fallback to default", ex);
                }
            }
            LogEngine.Instance.Debug("Database", "No saved settings found, returning defaults.");
            return new AppSettings();
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("Database", "Error loading app settings", ex);
            throw;
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        LogEngine.Instance.Info("Database", "Saving application settings...");
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();

            var json = JsonSerializer.Serialize(settings);
            var sql = @"
                INSERT INTO Settings (Key, Value)
                VALUES ('app_settings', @Value)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            ";

            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Value", json);
            await cmd.ExecuteNonQueryAsync();
            LogEngine.Instance.Info("Database", "Application settings saved successfully.");
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("Database", "Failed to save application settings", ex);
            throw;
        }
    }
    #endregion
}
