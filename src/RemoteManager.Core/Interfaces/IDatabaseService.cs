using RemoteManager.Core.Models;

namespace RemoteManager.Core.Interfaces;

public interface IDatabaseService
{
    Task InitializeAsync();

    // Connections
    Task<List<ConnectionItem>> GetAllConnectionsAsync();
    Task<ConnectionItem?> GetConnectionByIdAsync(Guid id);
    Task SaveConnectionAsync(ConnectionItem connection);
    Task UpdateConnectionsOrderAsync(IEnumerable<ConnectionItem> connections);
    Task DeleteConnectionAsync(Guid id);

    // Credentials
    Task<List<Credential>> GetAllCredentialsAsync();
    Task<Credential?> GetCredentialByIdAsync(Guid id);
    Task SaveCredentialAsync(Credential credential);
    Task DeleteCredentialAsync(Guid id);

    // Groups
    Task<List<ConnectionGroup>> GetAllGroupsAsync();
    Task SaveGroupAsync(ConnectionGroup group);
    Task DeleteGroupAsync(Guid id);

    // Settings
    Task<AppSettings> GetSettingsAsync();
    Task SaveSettingsAsync(AppSettings settings);

    // Maintenance / Bulk
    Task ClearAllDataAsync();
}
