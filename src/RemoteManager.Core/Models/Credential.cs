namespace RemoteManager.Core.Models;

public class Credential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? Domain { get; set; }
    public string EncryptedPassword { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public override string ToString() => string.IsNullOrWhiteSpace(Domain)
        ? $"{Title} ({Username})"
        : $"{Title} ({Domain}\\{Username})";
}
