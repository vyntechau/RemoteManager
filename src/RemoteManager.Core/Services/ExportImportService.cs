using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RemoteManager.Core.Interfaces;
using RemoteManager.Core.Logging;
using RemoteManager.Core.Models;

namespace RemoteManager.Core.Services;

public class ExportImportService : IExportImportService
{
    private readonly IDatabaseService _databaseService;
    private readonly IEncryptionService _encryptionService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ExportImportService(IDatabaseService databaseService, IEncryptionService encryptionService)
    {
        _databaseService = databaseService;
        _encryptionService = encryptionService;
    }

    #region JSON Export
    public async Task<string> ExportToJsonAsync(ExportOptions options)
    {
        LogEngine.Instance.Info("Database", "Generating JSON export package...");

        var allConnections = await _databaseService.GetAllConnectionsAsync();
        var allGroups = await _databaseService.GetAllGroupsAsync();
        var allCredentials = await _databaseService.GetAllCredentialsAsync();
        var settings = await _databaseService.GetSettingsAsync();

        var package = new ExportPackage
        {
            Version = "1.0",
            ExportedAt = DateTime.UtcNow,
            SourceApplication = "RemoteManager"
        };

        // 1. Connections
        if (options.IncludeConnections)
        {
            var selectedSet = options.SelectedConnectionIds != null && options.SelectedConnectionIds.Any()
                ? new HashSet<Guid>(options.SelectedConnectionIds)
                : null;

            package.Connections = selectedSet != null
                ? allConnections.Where(c => selectedSet.Contains(c.Id)).ToList()
                : allConnections.ToList();
        }

        // 2. Groups
        if (options.IncludeGroups)
        {
            if (options.IncludeConnections && package.Connections.Count > 0)
            {
                var usedGroupIds = package.Connections
                    .Where(c => c.GroupId.HasValue)
                    .Select(c => c.GroupId!.Value)
                    .ToHashSet();

                package.Groups = allGroups.Where(g => usedGroupIds.Contains(g.Id) || options.SelectedConnectionIds == null).ToList();
            }
            else
            {
                package.Groups = allGroups.ToList();
            }
        }

        // 3. Credentials
        if (options.IncludeCredentials)
        {
            var credsToExport = allCredentials.AsEnumerable();
            if (options.IncludeConnections && options.SelectedConnectionIds != null)
            {
                var usedCredIds = package.Connections
                    .Where(c => c.CredentialId.HasValue)
                    .Select(c => c.CredentialId!.Value)
                    .ToHashSet();

                credsToExport = credsToExport.Where(c => usedCredIds.Contains(c.Id));
            }

            var exportCreds = new List<ExportCredentialItem>();

            if (options.IncludePasswords && !string.IsNullOrWhiteSpace(options.Passphrase))
            {
                // Encrypt passwords using AES-256-GCM with PBKDF2 derived key
                byte[] salt = RandomNumberGenerator.GetBytes(16);
                byte[] nonce = RandomNumberGenerator.GetBytes(12);

                package.HasEncryptedCredentials = true;
                package.EncryptionAlgorithm = "AES-256-GCM-PBKDF2";
                package.EncryptionSalt = Convert.ToBase64String(salt);
                package.EncryptionNonce = Convert.ToBase64String(nonce);

                byte[] key = DeriveKey(options.Passphrase, salt);

                using var aesGcm = new AesGcm(key, 16);

                foreach (var cred in credsToExport)
                {
                    string? encryptedPassword = null;
                    if (!string.IsNullOrEmpty(cred.EncryptedPassword))
                    {
                        try
                        {
                            var plain = _encryptionService.Decrypt(cred.EncryptedPassword);
                            if (!string.IsNullOrEmpty(plain))
                            {
                                var plainBytes = Encoding.UTF8.GetBytes(plain);
                                var cipherBytes = new byte[plainBytes.Length];
                                var tag = new byte[16];

                                aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);

                                var combined = new byte[cipherBytes.Length + tag.Length];
                                Buffer.BlockCopy(cipherBytes, 0, combined, 0, cipherBytes.Length);
                                Buffer.BlockCopy(tag, 0, combined, cipherBytes.Length, tag.Length);

                                encryptedPassword = Convert.ToBase64String(combined);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogEngine.Instance.Warn("Database", $"Could not decrypt credential '{cred.Title}' during export: {ex.Message}");
                        }
                    }

                    exportCreds.Add(new ExportCredentialItem
                    {
                        Id = cred.Id,
                        Title = cred.Title,
                        Username = cred.Username,
                        Domain = cred.Domain,
                        EncryptedPassword = encryptedPassword,
                        Notes = cred.Notes,
                        CreatedAt = cred.CreatedAt,
                        UpdatedAt = cred.UpdatedAt
                    });
                }
            }
            else
            {
                package.HasEncryptedCredentials = false;
                package.EncryptionAlgorithm = options.IncludePasswords ? "DPAPI" : null;

                foreach (var cred in credsToExport)
                {
                    exportCreds.Add(new ExportCredentialItem
                    {
                        Id = cred.Id,
                        Title = cred.Title,
                        Username = cred.Username,
                        Domain = cred.Domain,
                        EncryptedPassword = options.IncludePasswords ? cred.EncryptedPassword : null,
                        Notes = cred.Notes,
                        CreatedAt = cred.CreatedAt,
                        UpdatedAt = cred.UpdatedAt
                    });
                }
            }

            package.Credentials = exportCreds;
        }

        // 4. Settings
        if (options.IncludeSettings)
        {
            package.Settings = settings;
        }

        return JsonSerializer.Serialize(package, JsonOptions);
    }

    public async Task ExportToJsonFileAsync(string filePath, ExportOptions options)
    {
        var json = await ExportToJsonAsync(options);
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
        LogEngine.Instance.Info("Database", $"Exported {options} to JSON file: {filePath}");
    }
    #endregion

    #region JSON Import
    public Task<ExportPackage?> ParseJsonPackageAsync(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Task.FromResult<ExportPackage?>(null);
        try
        {
            var package = JsonSerializer.Deserialize<ExportPackage>(json, JsonOptions);
            return Task.FromResult(package);
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("Database", "Failed to deserialize export JSON", ex);
            return Task.FromResult<ExportPackage?>(null);
        }
    }

    public async Task<ImportResult> ImportFromJsonAsync(string json, ImportOptions options)
    {
        var result = new ImportResult();
        var package = await ParseJsonPackageAsync(json);
        if (package == null)
        {
            result.Success = false;
            result.ErrorMessage = "The selected file is not a valid RemoteManager export package.";
            return result;
        }

        LogEngine.Instance.Info("Database", $"Starting JSON import with resolution: {options.ConflictResolution}");

        // If CleanAndReplace, clear existing tables first
        if (options.ConflictResolution == ImportConflictResolution.CleanAndReplace)
        {
            LogEngine.Instance.Info("Database", "Cleaning existing database records for CleanAndReplace import...");
            await _databaseService.ClearAllDataAsync();
        }

        // Prepare credentials lookup and decrypt if necessary
        var decryptedPasswords = new Dictionary<Guid, string>();
        if (package.HasEncryptedCredentials && package.Credentials.Count > 0)
        {
            if (package.EncryptionAlgorithm == "AES-256-GCM-PBKDF2")
            {
                if (string.IsNullOrEmpty(options.Passphrase))
                {
                    result.Success = false;
                    result.ErrorMessage = "This backup is protected with a passphrase. Please enter the master passphrase to decrypt credentials.";
                    return result;
                }

                if (string.IsNullOrEmpty(package.EncryptionSalt) || string.IsNullOrEmpty(package.EncryptionNonce))
                {
                    result.Success = false;
                    result.ErrorMessage = "Corrupted encryption parameters in export package.";
                    return result;
                }

                try
                {
                    byte[] salt = Convert.FromBase64String(package.EncryptionSalt);
                    byte[] nonce = Convert.FromBase64String(package.EncryptionNonce);
                    byte[] key = DeriveKey(options.Passphrase, salt);

                    using var aesGcm = new AesGcm(key, 16);

                    foreach (var cred in package.Credentials)
                    {
                        if (!string.IsNullOrEmpty(cred.EncryptedPassword))
                        {
                            var combined = Convert.FromBase64String(cred.EncryptedPassword);
                            if (combined.Length >= 16)
                            {
                                int cipherLen = combined.Length - 16;
                                var cipherBytes = new byte[cipherLen];
                                var tag = new byte[16];
                                Buffer.BlockCopy(combined, 0, cipherBytes, 0, cipherLen);
                                Buffer.BlockCopy(combined, cipherLen, tag, 0, 16);

                                var plainBytes = new byte[cipherLen];
                                aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);
                                var plain = Encoding.UTF8.GetString(plainBytes);
                                decryptedPasswords[cred.Id] = plain;
                            }
                        }
                    }
                }
                catch (CryptographicException)
                {
                    result.Success = false;
                    result.ErrorMessage = "Failed to decrypt credentials. The passphrase provided is incorrect.";
                    return result;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Error decrypting credentials: {ex.Message}";
                    return result;
                }
            }
        }

        // 1. Import Groups
        if (options.ImportGroups && package.Groups != null && package.Groups.Count > 0)
        {
            var existingGroups = await _databaseService.GetAllGroupsAsync();
            var existingById = existingGroups.ToDictionary(g => g.Id);
            var existingByName = existingGroups.ToDictionary(g => g.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var group in package.Groups)
            {
                if (existingById.TryGetValue(group.Id, out var existing))
                {
                    if (options.ConflictResolution == ImportConflictResolution.OverwriteExisting)
                    {
                        existing.Name = group.Name;
                        existing.ParentId = group.ParentId;
                        await _databaseService.SaveGroupAsync(existing);
                    }
                }
                else if (existingByName.TryGetValue(group.Name, out var existingNameMatch))
                {
                    if (options.ConflictResolution == ImportConflictResolution.OverwriteExisting)
                    {
                        existingNameMatch.ParentId = group.ParentId;
                        await _databaseService.SaveGroupAsync(existingNameMatch);
                    }
                }
                else
                {
                    await _databaseService.SaveGroupAsync(group);
                    result.GroupsImported++;
                    existingById[group.Id] = group;
                    existingByName[group.Name] = group;
                }
            }
        }

        // 2. Import Credentials
        if (options.ImportCredentials && package.Credentials != null && package.Credentials.Count > 0)
        {
            var existingCreds = await _databaseService.GetAllCredentialsAsync();
            var existingById = existingCreds.ToDictionary(c => c.Id);
            var existingByTitleUser = existingCreds.ToDictionary(c => $"{c.Title}::{c.Username}", StringComparer.OrdinalIgnoreCase);

            foreach (var expCred in package.Credentials)
            {
                string storedEncryptedPassword = string.Empty;

                if (decryptedPasswords.TryGetValue(expCred.Id, out var plainPass))
                {
                    storedEncryptedPassword = _encryptionService.Encrypt(plainPass);
                }
                else if (!string.IsNullOrEmpty(expCred.EncryptedPassword))
                {
                    // Check if local DPAPI can decrypt it directly (same machine export)
                    try
                    {
                        var decrypted = _encryptionService.Decrypt(expCred.EncryptedPassword);
                        storedEncryptedPassword = _encryptionService.Encrypt(decrypted);
                    }
                    catch
                    {
                        storedEncryptedPassword = expCred.EncryptedPassword;
                        result.Warnings.Add($"Password for credential '{expCred.Title}' could not be re-encrypted and may require re-entering.");
                    }
                }

                var key = $"{expCred.Title}::{expCred.Username}";
                if (existingById.TryGetValue(expCred.Id, out var existing))
                {
                    if (options.ConflictResolution == ImportConflictResolution.OverwriteExisting)
                    {
                        existing.Title = expCred.Title;
                        existing.Username = expCred.Username;
                        existing.Domain = expCred.Domain;
                        if (!string.IsNullOrEmpty(storedEncryptedPassword))
                        {
                            existing.EncryptedPassword = storedEncryptedPassword;
                        }
                        existing.Notes = expCred.Notes;
                        existing.UpdatedAt = DateTime.UtcNow;
                        await _databaseService.SaveCredentialAsync(existing);
                        result.CredentialsUpdated++;
                    }
                }
                else if (existingByTitleUser.TryGetValue(key, out var existingMatch))
                {
                    if (options.ConflictResolution == ImportConflictResolution.OverwriteExisting)
                    {
                        existingMatch.Domain = expCred.Domain;
                        if (!string.IsNullOrEmpty(storedEncryptedPassword))
                        {
                            existingMatch.EncryptedPassword = storedEncryptedPassword;
                        }
                        existingMatch.Notes = expCred.Notes;
                        existingMatch.UpdatedAt = DateTime.UtcNow;
                        await _databaseService.SaveCredentialAsync(existingMatch);
                        result.CredentialsUpdated++;
                    }
                }
                else
                {
                    var newCred = new Credential
                    {
                        Id = expCred.Id,
                        Title = expCred.Title,
                        Username = expCred.Username,
                        Domain = expCred.Domain,
                        EncryptedPassword = storedEncryptedPassword,
                        Notes = expCred.Notes,
                        CreatedAt = expCred.CreatedAt,
                        UpdatedAt = expCred.UpdatedAt
                    };
                    await _databaseService.SaveCredentialAsync(newCred);
                    result.CredentialsImported++;
                    existingById[newCred.Id] = newCred;
                    existingByTitleUser[key] = newCred;
                }
            }
        }

        // 3. Import Connections
        if (options.ImportConnections && package.Connections != null && package.Connections.Count > 0)
        {
            var existingConns = await _databaseService.GetAllConnectionsAsync();
            var existingById = existingConns.ToDictionary(c => c.Id);
            var existingByHostName = existingConns.ToDictionary(c => $"{c.Host}::{c.Name}::{c.Port}", StringComparer.OrdinalIgnoreCase);

            foreach (var conn in package.Connections)
            {
                var key = $"{conn.Host}::{conn.Name}::{conn.Port}";

                if (existingById.TryGetValue(conn.Id, out var existing))
                {
                    if (options.ConflictResolution == ImportConflictResolution.OverwriteExisting)
                    {
                        existing.Name = conn.Name;
                        existing.Protocol = conn.Protocol;
                        existing.Host = conn.Host;
                        existing.Port = conn.Port;
                        existing.GroupId = conn.GroupId;
                        existing.CredentialId = conn.CredentialId;
                        existing.DisplayMode = conn.DisplayMode;
                        existing.SettingsJson = conn.SettingsJson;
                        existing.IsBookmarked = conn.IsBookmarked;
                        existing.SortOrder = conn.SortOrder;
                        existing.UpdatedAt = DateTime.UtcNow;
                        await _databaseService.SaveConnectionAsync(existing);
                        result.ConnectionsUpdated++;
                    }
                    else
                    {
                        result.ConnectionsSkipped++;
                    }
                }
                else if (existingByHostName.TryGetValue(key, out var existingMatch))
                {
                    if (options.ConflictResolution == ImportConflictResolution.OverwriteExisting)
                    {
                        existingMatch.Protocol = conn.Protocol;
                        existingMatch.GroupId = conn.GroupId;
                        existingMatch.CredentialId = conn.CredentialId;
                        existingMatch.DisplayMode = conn.DisplayMode;
                        existingMatch.SettingsJson = conn.SettingsJson;
                        existingMatch.IsBookmarked = conn.IsBookmarked;
                        existingMatch.SortOrder = conn.SortOrder;
                        existingMatch.UpdatedAt = DateTime.UtcNow;
                        await _databaseService.SaveConnectionAsync(existingMatch);
                        result.ConnectionsUpdated++;
                    }
                    else
                    {
                        result.ConnectionsSkipped++;
                    }
                }
                else
                {
                    await _databaseService.SaveConnectionAsync(conn);
                    result.ConnectionsImported++;
                    existingById[conn.Id] = conn;
                    existingByHostName[key] = conn;
                }
            }
        }

        // 4. Import Settings
        if (options.ImportSettings && package.Settings != null)
        {
            await _databaseService.SaveSettingsAsync(package.Settings);
            result.SettingsImported = true;
        }

        result.Success = true;
        LogEngine.Instance.Info("Database", $"JSON Import completed. {result.SummaryText}");
        return result;
    }

    public async Task<ImportResult> ImportFromJsonFileAsync(string filePath, ImportOptions options)
    {
        if (!File.Exists(filePath))
        {
            return new ImportResult { Success = false, ErrorMessage = $"File not found: {filePath}" };
        }
        var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
        return await ImportFromJsonAsync(json, options);
    }
    #endregion

    #region CSV Export & Import
    public async Task<string> ExportToCsvAsync(IEnumerable<ConnectionItem>? connections = null)
    {
        var conns = connections?.ToList() ?? await _databaseService.GetAllConnectionsAsync();
        var groups = (await _databaseService.GetAllGroupsAsync()).ToDictionary(g => g.Id, g => g.Name);
        var creds = (await _databaseService.GetAllCredentialsAsync()).ToDictionary(c => c.Id);

        var sb = new StringBuilder();
        // RFC 4180 header
        sb.AppendLine("Name,Protocol,Host,Port,Group,Username,Domain,DisplayMode,IsBookmarked,Notes");

        foreach (var c in conns)
        {
            var groupName = c.GroupId.HasValue && groups.TryGetValue(c.GroupId.Value, out var gName) ? gName : string.Empty;
            var username = string.Empty;
            var domain = string.Empty;
            var notes = string.Empty;

            if (c.CredentialId.HasValue && creds.TryGetValue(c.CredentialId.Value, out var cr))
            {
                username = cr.Username;
                domain = cr.Domain ?? string.Empty;
                notes = cr.Notes ?? string.Empty;
            }

            sb.AppendLine(string.Join(",",
                EscapeCsv(c.Name),
                EscapeCsv(c.Protocol.ToString()),
                EscapeCsv(c.Host),
                c.Port.ToString(),
                EscapeCsv(groupName),
                EscapeCsv(username),
                EscapeCsv(domain),
                EscapeCsv(c.DisplayMode.ToString()),
                c.IsBookmarked ? "true" : "false",
                EscapeCsv(notes)
            ));
        }

        return sb.ToString();
    }

    public async Task ExportToCsvFileAsync(string filePath, IEnumerable<ConnectionItem>? connections = null)
    {
        var csv = await ExportToCsvAsync(connections);
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(filePath, csv, Encoding.UTF8);
    }

    public async Task<ImportResult> ImportFromCsvAsync(string csvContent, ImportConflictResolution conflictResolution = ImportConflictResolution.MergeAndKeepExisting)
    {
        var result = new ImportResult();
        if (string.IsNullOrWhiteSpace(csvContent))
        {
            result.Success = false;
            result.ErrorMessage = "The CSV file is empty.";
            return result;
        }

        var lines = SplitCsvLines(csvContent);
        if (lines.Count < 2)
        {
            result.Success = false;
            result.ErrorMessage = "CSV does not contain any data rows.";
            return result;
        }

        if (conflictResolution == ImportConflictResolution.CleanAndReplace)
        {
            await _databaseService.ClearAllDataAsync();
        }

        var existingGroups = (await _databaseService.GetAllGroupsAsync()).ToDictionary(g => g.Name, StringComparer.OrdinalIgnoreCase);
        var existingCreds = (await _databaseService.GetAllCredentialsAsync()).ToDictionary(c => $"{c.Username}::{c.Domain}", StringComparer.OrdinalIgnoreCase);
        var existingConns = (await _databaseService.GetAllConnectionsAsync()).ToDictionary(c => $"{c.Host}::{c.Name}::{c.Port}", StringComparer.OrdinalIgnoreCase);

        // Header mapping
        var header = ParseCsvRow(lines[0]);
        var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Count; i++)
        {
            colMap[header[i].Trim()] = i;
        }

        for (int rowIdx = 1; rowIdx < lines.Count; rowIdx++)
        {
            var row = ParseCsvRow(lines[rowIdx]);
            if (row.Count == 0 || (row.Count == 1 && string.IsNullOrWhiteSpace(row[0]))) continue;

            string GetCol(string colName, string def = "") =>
                colMap.TryGetValue(colName, out var idx) && idx < row.Count ? row[idx] : def;

            var name = GetCol("Name");
            var host = GetCol("Host");
            if (string.IsNullOrWhiteSpace(host)) continue;

            var protocolStr = GetCol("Protocol", "RDP");
            if (!Enum.TryParse<ProtocolType>(protocolStr, true, out var protocol))
            {
                protocol = ProtocolType.RDP;
            }

            var portStr = GetCol("Port");
            if (!int.TryParse(portStr, out var port) || port <= 0)
            {
                port = protocol switch
                {
                    ProtocolType.SSH => 22,
                    ProtocolType.VNC => 5900,
                    ProtocolType.Web => 443,
                    _ => 3389
                };
            }

            var groupName = GetCol("Group");
            Guid? groupId = null;
            if (!string.IsNullOrWhiteSpace(groupName))
            {
                if (!existingGroups.TryGetValue(groupName, out var group))
                {
                    group = new ConnectionGroup { Name = groupName };
                    await _databaseService.SaveGroupAsync(group);
                    existingGroups[groupName] = group;
                    result.GroupsImported++;
                }
                groupId = group.Id;
            }

            var username = GetCol("Username");
            var domain = GetCol("Domain");
            var notes = GetCol("Notes");
            Guid? credentialId = null;
            if (!string.IsNullOrWhiteSpace(username))
            {
                var credKey = $"{username}::{domain}";
                if (!existingCreds.TryGetValue(credKey, out var cred))
                {
                    cred = new Credential
                    {
                        Title = string.IsNullOrWhiteSpace(name) ? username : $"{name} Credential",
                        Username = username,
                        Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
                        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes
                    };
                    await _databaseService.SaveCredentialAsync(cred);
                    existingCreds[credKey] = cred;
                    result.CredentialsImported++;
                }
                credentialId = cred.Id;
            }

            var displayModeStr = GetCol("DisplayMode", "Tabbed");
            if (!Enum.TryParse<DisplayMode>(displayModeStr, true, out var displayMode))
            {
                displayMode = DisplayMode.Tabbed;
            }

            var isBookmarked = bool.TryParse(GetCol("IsBookmarked"), out var bm) && bm;

            var connKey = $"{host}::{name}::{port}";
            if (existingConns.TryGetValue(connKey, out var existingConn))
            {
                if (conflictResolution == ImportConflictResolution.OverwriteExisting)
                {
                    existingConn.Protocol = protocol;
                    existingConn.GroupId = groupId;
                    existingConn.CredentialId = credentialId;
                    existingConn.DisplayMode = displayMode;
                    existingConn.IsBookmarked = isBookmarked;
                    existingConn.UpdatedAt = DateTime.UtcNow;
                    await _databaseService.SaveConnectionAsync(existingConn);
                    result.ConnectionsUpdated++;
                }
                else
                {
                    result.ConnectionsSkipped++;
                }
            }
            else
            {
                var newConn = new ConnectionItem
                {
                    Name = string.IsNullOrWhiteSpace(name) ? host : name,
                    Host = host,
                    Port = port,
                    Protocol = protocol,
                    GroupId = groupId,
                    CredentialId = credentialId,
                    DisplayMode = displayMode,
                    IsBookmarked = isBookmarked
                };
                await _databaseService.SaveConnectionAsync(newConn);
                existingConns[connKey] = newConn;
                result.ConnectionsImported++;
            }
        }

        result.Success = true;
        LogEngine.Instance.Info("Database", $"CSV Import completed. {result.SummaryText}");
        return result;
    }

    public async Task<ImportResult> ImportFromCsvFileAsync(string filePath, ImportConflictResolution conflictResolution = ImportConflictResolution.MergeAndKeepExisting)
    {
        if (!File.Exists(filePath))
        {
            return new ImportResult { Success = false, ErrorMessage = $"File not found: {filePath}" };
        }
        var csv = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
        return await ImportFromCsvAsync(csv, conflictResolution);
    }
    #endregion

    #region RDP File (.rdp) Export & Import
    public async Task<string> ExportToRdpAsync(ConnectionItem connection, Credential? credential = null)
    {
        if (credential == null && connection.CredentialId.HasValue)
        {
            credential = await _databaseService.GetCredentialByIdAsync(connection.CredentialId.Value);
        }

        var sb = new StringBuilder();
        sb.AppendLine("screen mode id:i:2");
        sb.AppendLine("use multimon:i:0");
        sb.AppendLine("desktopwidth:i:1920");
        sb.AppendLine("desktopheight:i:1080");
        sb.AppendLine("session bpp:i:32");
        sb.AppendLine("winposstr:s:0,3,0,0,800,600");
        sb.AppendLine("compression:i:1");
        sb.AppendLine("keyboardhook:i:1");
        sb.AppendLine("audiomode:i:0");
        sb.AppendLine("redirectprinters:i:0");
        sb.AppendLine("redirectcomports:i:0");
        sb.AppendLine("redirectsmartcards:i:0");
        sb.AppendLine("redirectclipboard:i:1");
        sb.AppendLine("redirectposdevices:i:0");
        sb.AppendLine("redirectdrives:i:1");
        sb.AppendLine("drivestoredirect:s:*");
        sb.AppendLine("autoreconnection enabled:i:1");
        sb.AppendLine("authentication level:i:2");
        sb.AppendLine("prompt for credentials:i:0");
        sb.AppendLine("negotiate security layer:i:1");
        sb.AppendLine("remoteapplicationmode:i:0");
        sb.AppendLine("alternate shell:s:");
        sb.AppendLine("shell working directory:s:");
        sb.AppendLine("gatewayhostname:s:");
        sb.AppendLine("gatewayusagemethod:i:0");
        sb.AppendLine("gatewaycredentialssource:i:4");
        sb.AppendLine("gatewayprofileusagemethod:i:0");
        sb.AppendLine("promptcredentialonce:i:0");

        sb.AppendLine($"full address:s:{connection.Host}:{connection.Port}");

        if (credential != null)
        {
            if (!string.IsNullOrWhiteSpace(credential.Username))
            {
                sb.AppendLine($"username:s:{credential.Username}");
            }
            if (!string.IsNullOrWhiteSpace(credential.Domain))
            {
                sb.AppendLine($"domain:s:{credential.Domain}");
            }
        }

        return sb.ToString();
    }

    public async Task ExportToRdpFileAsync(string filePath, ConnectionItem connection, Credential? credential = null)
    {
        var rdp = await ExportToRdpAsync(connection, credential);
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(filePath, rdp, Encoding.Unicode);
    }

    public async Task<ConnectionItem> ImportFromRdpAsync(string rdpContent, string? name = null)
    {
        var lines = rdpContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        string host = "localhost";
        int port = 3389;
        string? username = null;
        string? domain = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("full address:s:", StringComparison.OrdinalIgnoreCase))
            {
                var addr = trimmed.Substring("full address:s:".Length).Trim();
                if (addr.Contains(':'))
                {
                    var parts = addr.Split(':');
                    host = parts[0];
                    if (int.TryParse(parts[1], out var parsedPort)) port = parsedPort;
                }
                else
                {
                    host = addr;
                }
            }
            else if (trimmed.StartsWith("username:s:", StringComparison.OrdinalIgnoreCase))
            {
                username = trimmed.Substring("username:s:".Length).Trim();
            }
            else if (trimmed.StartsWith("domain:s:", StringComparison.OrdinalIgnoreCase))
            {
                domain = trimmed.Substring("domain:s:".Length).Trim();
            }
        }

        Guid? credentialId = null;
        if (!string.IsNullOrWhiteSpace(username))
        {
            var creds = await _databaseService.GetAllCredentialsAsync();
            var existing = creds.FirstOrDefault(c =>
                string.Equals(c.Username, username, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Domain ?? "", domain ?? "", StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                credentialId = existing.Id;
            }
            else
            {
                var newCred = new Credential
                {
                    Title = string.IsNullOrWhiteSpace(name) ? username : $"{name} Credential",
                    Username = username,
                    Domain = domain
                };
                await _databaseService.SaveCredentialAsync(newCred);
                credentialId = newCred.Id;
            }
        }

        var conn = new ConnectionItem
        {
            Name = string.IsNullOrWhiteSpace(name) ? host : name,
            Host = host,
            Port = port,
            Protocol = ProtocolType.RDP,
            CredentialId = credentialId,
            DisplayMode = DisplayMode.Tabbed
        };

        await _databaseService.SaveConnectionAsync(conn);
        return conn;
    }

    public async Task<ConnectionItem> ImportFromRdpFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("RDP file not found", filePath);
        }
        var content = await File.ReadAllTextAsync(filePath);
        var name = Path.GetFileNameWithoutExtension(filePath);
        return await ImportFromRdpAsync(content, name);
    }
    #endregion

    #region Helpers
    private static byte[] DeriveKey(string passphrase, byte[] salt)
    {
        using var kdf = new Rfc2898DeriveBytes(passphrase, salt, 100_000, HashAlgorithmName.SHA256);
        return kdf.GetBytes(32); // 256 bits
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }

    private static List<string> SplitCsvLines(string content)
    {
        var lines = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                sb.Append(c);
            }
            else if ((c == '\n' || c == '\r') && !inQuotes)
            {
                if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
                {
                    i++;
                }
                if (sb.Length > 0)
                {
                    lines.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 0)
        {
            lines.Add(sb.ToString());
        }

        return lines;
    }

    private static List<string> ParseCsvRow(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        result.Add(sb.ToString());
        return result;
    }
    #endregion
}
