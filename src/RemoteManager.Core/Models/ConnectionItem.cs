namespace RemoteManager.Core.Models;

public class ConnectionItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public ProtocolType Protocol { get; set; } = ProtocolType.RDP;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 3389;
    public Guid? GroupId { get; set; }
    public Guid? CredentialId { get; set; }
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Tabbed;
    public string? SettingsJson { get; set; }
    public bool IsBookmarked { get; set; } = false;
    public int SortOrder { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation/presentation helper
    public string FullAddress => Port > 0 ? $"{Host}:{Port}" : Host;

    public string DisplayName
    {
        get
        {
            var raw = string.IsNullOrWhiteSpace(Name) ? Host : Name;
            var prefix = $"{Protocol}:";
            if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var stripped = raw.Substring(prefix.Length).TrimStart();
                if (!string.IsNullOrWhiteSpace(stripped)) return stripped;
            }
            return raw;
        }
    }

    public ConnectionItem Clone()
    {
        return new ConnectionItem
        {
            Id = Guid.NewGuid(),
            Name = $"{Name} (Copy)",
            Protocol = Protocol,
            Host = Host,
            Port = Port,
            GroupId = GroupId,
            CredentialId = CredentialId,
            DisplayMode = DisplayMode,
            SettingsJson = SettingsJson,
            IsBookmarked = IsBookmarked,
            SortOrder = SortOrder,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
