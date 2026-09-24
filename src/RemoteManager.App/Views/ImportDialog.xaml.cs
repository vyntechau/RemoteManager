using System.IO;
using System.Windows;
using Microsoft.Win32;
using RemoteManager.Core.Interfaces;
using RemoteManager.Core.Models;

namespace RemoteManager.App.Views;

public partial class ImportDialog : Window
{
    private readonly IExportImportService _exportImportService;
    private ExportPackage? _detectedJsonPackage;
    private string? _selectedFilePath;
    private string _fileType = string.Empty;

    public bool WasImportSuccessful { get; private set; }
    public ImportResult? Result { get; private set; }

    public ImportDialog(IExportImportService exportImportService)
    {
        InitializeComponent();
        _exportImportService = exportImportService;
    }

    private async void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        HideError();

        var dialog = new OpenFileDialog
        {
            Title = "Select File to Import",
            Filter = "All Supported Files (*.json;*.csv;*.rdp)|*.json;*.csv;*.rdp|RemoteManager Backup (*.json)|*.json|CSV Spreadsheet (*.csv)|*.csv|Remote Desktop Connection (*.rdp)|*.rdp|All Files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            _selectedFilePath = dialog.FileName;
            FilePathTextBox.Text = _selectedFilePath;
            await InspectSelectedFileAsync(_selectedFilePath);
        }
    }

    private async Task InspectSelectedFileAsync(string filePath)
    {
        HideError();
        _detectedJsonPackage = null;
        ImportButton.IsEnabled = false;

        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        if (ext == ".json")
        {
            _fileType = "json";
            FormatBadgeText.Text = "JSON Backup";
            try
            {
                var content = await File.ReadAllTextAsync(filePath);
                _detectedJsonPackage = await _exportImportService.ParseJsonPackageAsync(content);

                if (_detectedJsonPackage == null)
                {
                    ShowError("The selected JSON file does not match the RemoteManager package format.");
                    FileDetailsPanel.Visibility = Visibility.Collapsed;
                    return;
                }

                FileSummaryText.Text = $"RemoteManager Backup (v{_detectedJsonPackage.Version})";
                FileDetailText.Text = $"Exported: {_detectedJsonPackage.ExportedAt:yyyy-MM-dd HH:mm:ss UTC}\n" +
                                       $"Contains: {_detectedJsonPackage.Connections?.Count ?? 0} connections, " +
                                       $"{_detectedJsonPackage.Groups?.Count ?? 0} groups, " +
                                       $"{_detectedJsonPackage.Credentials?.Count ?? 0} credentials" +
                                       (_detectedJsonPackage.Settings != null ? ", settings included" : "");

                EncryptedPassphrasePanel.Visibility = _detectedJsonPackage.HasEncryptedCredentials
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                JsonContentSelectionPanel.Visibility = Visibility.Visible;
                FileDetailsPanel.Visibility = Visibility.Visible;
                ImportButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                ShowError($"Failed to read JSON file: {ex.Message}");
                FileDetailsPanel.Visibility = Visibility.Collapsed;
            }
        }
        else if (ext == ".csv")
        {
            _fileType = "csv";
            FormatBadgeText.Text = "CSV Spreadsheet";
            FileSummaryText.Text = "Tabular Connections List";
            try
            {
                var lines = await File.ReadAllLinesAsync(filePath);
                var rowCount = Math.Max(0, lines.Length - 1);
                FileDetailText.Text = $"Detected {rowCount} data rows.\nGroup names and usernames will be automatically mapped to groups and credentials.";

                EncryptedPassphrasePanel.Visibility = Visibility.Collapsed;
                JsonContentSelectionPanel.Visibility = Visibility.Collapsed;
                FileDetailsPanel.Visibility = Visibility.Visible;
                ImportButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                ShowError($"Failed to read CSV file: {ex.Message}");
                FileDetailsPanel.Visibility = Visibility.Collapsed;
            }
        }
        else if (ext == ".rdp")
        {
            _fileType = "rdp";
            FormatBadgeText.Text = "Remote Desktop (.rdp)";
            FileSummaryText.Text = "Standard Microsoft RDP File";
            FileDetailText.Text = $"Connection host and credentials will be imported as a new RDP server connection.";

            EncryptedPassphrasePanel.Visibility = Visibility.Collapsed;
            JsonContentSelectionPanel.Visibility = Visibility.Collapsed;
            FileDetailsPanel.Visibility = Visibility.Visible;
            ImportButton.IsEnabled = true;
        }
        else
        {
            ShowError("Unsupported file type. Please select a .json, .csv, or .rdp file.");
            FileDetailsPanel.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedFilePath) || !File.Exists(_selectedFilePath))
        {
            ShowError("Please select a valid file to import.");
            return;
        }

        HideError();

        var conflict = ConflictReplaceRadio.IsChecked == true
            ? ImportConflictResolution.CleanAndReplace
            : ConflictOverwriteRadio.IsChecked == true
                ? ImportConflictResolution.OverwriteExisting
                : ImportConflictResolution.MergeAndKeepExisting;

        if (conflict == ImportConflictResolution.CleanAndReplace)
        {
            var confirm = MessageBox.Show(
                "Are you sure you want to clean and replace all data? All existing connections, groups, and credentials in RemoteManager will be deleted.",
                "Confirm Clean and Replace",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;
        }

        try
        {
            ImportResult importResult;

            if (_fileType == "json")
            {
                var options = new ImportOptions
                {
                    ImportConnections = ImportConnectionsCheckBox.IsChecked == true,
                    ImportGroups = ImportGroupsCheckBox.IsChecked == true,
                    ImportCredentials = ImportCredentialsCheckBox.IsChecked == true,
                    ImportSettings = ImportSettingsCheckBox.IsChecked == true,
                    ConflictResolution = conflict,
                    Passphrase = ImportPassphraseBox.Password
                };

                importResult = await _exportImportService.ImportFromJsonFileAsync(_selectedFilePath, options);
            }
            else if (_fileType == "csv")
            {
                importResult = await _exportImportService.ImportFromCsvFileAsync(_selectedFilePath, conflict);
            }
            else if (_fileType == "rdp")
            {
                var conn = await _exportImportService.ImportFromRdpFileAsync(_selectedFilePath);
                importResult = new ImportResult
                {
                    Success = true,
                    ConnectionsImported = 1,
                    CredentialsImported = conn.CredentialId.HasValue ? 1 : 0
                };
            }
            else
            {
                ShowError("Unknown file type.");
                return;
            }

            if (!importResult.Success)
            {
                ShowError(importResult.ErrorMessage ?? "Import failed.");
                return;
            }

            WasImportSuccessful = true;
            Result = importResult;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError($"Import error: {ex.Message}");
        }
    }

    private void ShowError(string msg)
    {
        ErrorText.Text = msg;
        ErrorBanner.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorBanner.Visibility = Visibility.Collapsed;
        ErrorText.Text = "";
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
