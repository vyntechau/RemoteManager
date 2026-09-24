namespace RemoteManager.Core.Models;

/// <summary>
/// Root data transfer object representing a full or partial export of RemoteManager data.
/// </summary>
public class ExportPackage
{
    public string Version { get; set; } = "1.0";
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
    public string SourceApplication { get; set; } = "RemoteManager";
    public List<ConnectionItem> Connections { get; set; } = [];
    public List<ConnectionGroup> Groups { get; set; } = [];
    public List<ExportCredentialItem> Credentials { get; set; } = [];
    public AppSettings? Settings { get; set; }

    /// <summary>
    /// Indicates whether the credential passwords in this export are encrypted with a custom passphrase (AES-256-GCM).
    /// </summary>
    public bool HasEncryptedCredentials { get; set; }

    /// <summary>
    /// Cryptographic algorithm used (e.g. "AES-256-GCM-PBKDF2" or "DPAPI").
    /// </summary>
    public string? EncryptionAlgorithm { get; set; }

    /// <summary>
    /// Base64-encoded PBKDF2 salt for key derivation.
    /// </summary>
    public string? EncryptionSalt { get; set; }

    /// <summary>
    /// Base64-encoded initialization vector / nonce for AES-GCM.
    /// </summary>
    public string? EncryptionNonce { get; set; }
}

/// <summary>
/// Credential representation for export packages.
/// </summary>
public class ExportCredentialItem
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? Domain { get; set; }
    public string? EncryptedPassword { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Options controlling what data is exported and how credentials are protected.
/// </summary>
public class ExportOptions
{
    public bool IncludeConnections { get; set; } = true;
    public bool IncludeGroups { get; set; } = true;
    public bool IncludeCredentials { get; set; } = true;
    public bool IncludePasswords { get; set; } = true;
    public bool IncludeSettings { get; set; } = false;

    /// <summary>
    /// Optional passphrase to encrypt credential passwords using AES-256-GCM.
    /// If null or empty, passwords will either use local DPAPI or be omitted based on <see cref="IncludePasswords"/>.
    /// </summary>
    public string? Passphrase { get; set; }

    /// <summary>
    /// Specific connection IDs to export. If null or empty, all connections are exported.
    /// </summary>
    public IEnumerable<Guid>? SelectedConnectionIds { get; set; }
}

/// <summary>
/// Defines how conflicts are resolved when importing data.
/// </summary>
public enum ImportConflictResolution
{
    /// <summary>
    /// Skip items if an item with the same ID or same Name and Host already exists.
    /// </summary>
    MergeAndKeepExisting,

    /// <summary>
    /// Overwrite existing items if an item with the same ID or same Name and Host exists.
    /// </summary>
    OverwriteExisting,

    /// <summary>
    /// Clear all existing connections, groups, and credentials before importing.
    /// </summary>
    CleanAndReplace
}

/// <summary>
/// Options controlling how data is imported.
/// </summary>
public class ImportOptions
{
    public bool ImportConnections { get; set; } = true;
    public bool ImportGroups { get; set; } = true;
    public bool ImportCredentials { get; set; } = true;
    public bool ImportSettings { get; set; } = false;
    public ImportConflictResolution ConflictResolution { get; set; } = ImportConflictResolution.MergeAndKeepExisting;

    /// <summary>
    /// Passphrase used to decrypt credentials if the package is encrypted.
    /// </summary>
    public string? Passphrase { get; set; }
}

/// <summary>
/// Detailed summary of an import operation.
/// </summary>
public class ImportResult
{
    public bool Success { get; set; }
    public int ConnectionsImported { get; set; }
    public int ConnectionsUpdated { get; set; }
    public int ConnectionsSkipped { get; set; }
    public int GroupsImported { get; set; }
    public int CredentialsImported { get; set; }
    public int CredentialsUpdated { get; set; }
    public bool SettingsImported { get; set; }
    public List<string> Warnings { get; set; } = [];
    public string? ErrorMessage { get; set; }

    public string SummaryText
    {
        get
        {
            if (!Success)
            {
                return $"Import failed: {ErrorMessage ?? "Unknown error"}";
            }

            var parts = new List<string>();
            if (ConnectionsImported > 0) parts.Add($"{ConnectionsImported} connections added");
            if (ConnectionsUpdated > 0) parts.Add($"{ConnectionsUpdated} connections updated");
            if (ConnectionsSkipped > 0) parts.Add($"{ConnectionsSkipped} connections skipped");
            if (GroupsImported > 0) parts.Add($"{GroupsImported} groups added");
            if (CredentialsImported > 0) parts.Add($"{CredentialsImported} credentials added");
            if (CredentialsUpdated > 0) parts.Add($"{CredentialsUpdated} credentials updated");
            if (SettingsImported) parts.Add("Settings restored");

            if (parts.Count == 0) return "No items were imported.";
            return string.Join(", ", parts) + ".";
        }
    }
}
