using RemoteManager.Core.Models;

namespace RemoteManager.Core.Interfaces;

/// <summary>
/// Service contract for exporting and importing RemoteManager configurations,
/// connections, credentials, and settings across JSON, CSV, and RDP formats.
/// </summary>
public interface IExportImportService
{
    /// <summary>
    /// Exports data to a JSON string representation based on provided options.
    /// </summary>
    Task<string> ExportToJsonAsync(ExportOptions options);

    /// <summary>
    /// Exports data to a JSON file.
    /// </summary>
    Task ExportToJsonFileAsync(string filePath, ExportOptions options);

    /// <summary>
    /// Exports connections to a standard CSV string representation.
    /// </summary>
    Task<string> ExportToCsvAsync(IEnumerable<ConnectionItem>? connections = null);

    /// <summary>
    /// Exports connections to a standard CSV file.
    /// </summary>
    Task ExportToCsvFileAsync(string filePath, IEnumerable<ConnectionItem>? connections = null);

    /// <summary>
    /// Exports an individual connection (specifically RDP) to an .rdp file format string.
    /// </summary>
    Task<string> ExportToRdpAsync(ConnectionItem connection, Credential? credential = null);

    /// <summary>
    /// Exports an individual connection to an .rdp file.
    /// </summary>
    Task ExportToRdpFileAsync(string filePath, ConnectionItem connection, Credential? credential = null);

    /// <summary>
    /// Parses an export JSON string into an <see cref="ExportPackage"/> for inspection or preview.
    /// </summary>
    Task<ExportPackage?> ParseJsonPackageAsync(string json);

    /// <summary>
    /// Imports data from a JSON string using the specified import options and conflict resolution strategy.
    /// </summary>
    Task<ImportResult> ImportFromJsonAsync(string json, ImportOptions options);

    /// <summary>
    /// Imports data from a JSON file.
    /// </summary>
    Task<ImportResult> ImportFromJsonFileAsync(string filePath, ImportOptions options);

    /// <summary>
    /// Imports connections from a CSV string.
    /// </summary>
    Task<ImportResult> ImportFromCsvAsync(string csvContent, ImportConflictResolution conflictResolution = ImportConflictResolution.MergeAndKeepExisting);

    /// <summary>
    /// Imports connections from a CSV file.
    /// </summary>
    Task<ImportResult> ImportFromCsvFileAsync(string filePath, ImportConflictResolution conflictResolution = ImportConflictResolution.MergeAndKeepExisting);

    /// <summary>
    /// Imports an RDP configuration string into a new <see cref="ConnectionItem"/>.
    /// </summary>
    Task<ConnectionItem> ImportFromRdpAsync(string rdpContent, string? name = null);

    /// <summary>
    /// Imports an .rdp file into a new <see cref="ConnectionItem"/> and persists it to database.
    /// </summary>
    Task<ConnectionItem> ImportFromRdpFileAsync(string filePath);
}
