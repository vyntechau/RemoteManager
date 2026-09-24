using System.Windows;
using Microsoft.Win32;
using RemoteManager.Core.Interfaces;
using RemoteManager.Core.Models;

namespace RemoteManager.App.Views;

public partial class ExportDialog : Window
{
    private readonly IExportImportService _exportImportService;
    private readonly IEnumerable<Guid>? _selectedConnectionIds;
    private bool _isLoaded;

    public bool WasExportSuccessful { get; private set; }
    public string? ExportedFilePath { get; private set; }

    public ExportDialog(IExportImportService exportImportService, IEnumerable<Guid>? selectedConnectionIds = null)
    {
        InitializeComponent();
        _exportImportService = exportImportService;
        _selectedConnectionIds = selectedConnectionIds;
        _isLoaded = true;
        UpdateUiState();
    }

    private void OnFormatChanged(object sender, RoutedEventArgs e)
    {
        UpdateUiState();
    }

    private void OnSelectionChanged(object sender, RoutedEventArgs e)
    {
        UpdateUiState();
    }

    private void OnPassphraseOptionChanged(object sender, RoutedEventArgs e)
    {
        UpdateUiState();
    }

    private void UpdateUiState()
    {
        if (!_isLoaded ||
            FormatJsonRadio == null ||
            FormatCsvRadio == null ||
            SecurityProtectionPanel == null ||
            GroupsCheckBox == null ||
            SettingsCheckBox == null ||
            CredentialsCheckBox == null ||
            EncryptWithPassphraseCheckBox == null ||
            PassphraseInputsPanel == null ||
            IncludePasswordsWithoutPassphraseCheckBox == null)
        {
            return;
        }

        bool isJson = FormatJsonRadio.IsChecked == true;
        bool hasCreds = CredentialsCheckBox.IsChecked == true;

        SecurityProtectionPanel.Visibility = (isJson && hasCreds) ? Visibility.Visible : Visibility.Collapsed;
        GroupsCheckBox.IsEnabled = isJson;
        SettingsCheckBox.IsEnabled = isJson;

        bool encrypt = EncryptWithPassphraseCheckBox.IsChecked == true;
        PassphraseInputsPanel.Visibility = encrypt ? Visibility.Visible : Visibility.Collapsed;
        IncludePasswordsWithoutPassphraseCheckBox.Visibility = encrypt ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        PassphraseErrorText.Visibility = Visibility.Collapsed;

        bool isJson = FormatJsonRadio.IsChecked == true;

        if (isJson)
        {
            var options = new ExportOptions
            {
                IncludeConnections = ConnectionsCheckBox.IsChecked == true,
                IncludeGroups = GroupsCheckBox.IsChecked == true,
                IncludeCredentials = CredentialsCheckBox.IsChecked == true,
                IncludeSettings = SettingsCheckBox.IsChecked == true,
                SelectedConnectionIds = _selectedConnectionIds
            };

            if (options.IncludeCredentials)
            {
                if (EncryptWithPassphraseCheckBox.IsChecked == true)
                {
                    var pass = PassphraseBox.Password;
                    var confirm = ConfirmPassphraseBox.Password;

                    if (string.IsNullOrWhiteSpace(pass))
                    {
                        PassphraseErrorText.Text = "Please enter a passphrase or uncheck passphrase protection.";
                        PassphraseErrorText.Visibility = Visibility.Visible;
                        return;
                    }

                    if (pass != confirm)
                    {
                        PassphraseErrorText.Text = "Passphrases do not match.";
                        PassphraseErrorText.Visibility = Visibility.Visible;
                        return;
                    }

                    options.IncludePasswords = true;
                    options.Passphrase = pass;
                }
                else
                {
                    options.IncludePasswords = IncludePasswordsWithoutPassphraseCheckBox.IsChecked == true;
                    options.Passphrase = null;
                }
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export RemoteManager Data",
                Filter = "RemoteManager Backup Package (*.json)|*.json|All Files (*.*)|*.*",
                FileName = $"RemoteManager_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                DefaultExt = ".json"
            };

            if (dialog.ShowDialog(this) == true)
            {
                try
                {
                    await _exportImportService.ExportToJsonFileAsync(dialog.FileName, options);
                    WasExportSuccessful = true;
                    ExportedFilePath = dialog.FileName;
                    DialogResult = true;
                    Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to export backup: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        else
        {
            // CSV Export
            var dialog = new SaveFileDialog
            {
                Title = "Export Connections to CSV",
                Filter = "CSV Spreadsheet (*.csv)|*.csv|All Files (*.*)|*.*",
                FileName = $"RemoteManager_Connections_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                DefaultExt = ".csv"
            };

            if (dialog.ShowDialog(this) == true)
            {
                try
                {
                    await _exportImportService.ExportToCsvFileAsync(dialog.FileName);
                    WasExportSuccessful = true;
                    ExportedFilePath = dialog.FileName;
                    DialogResult = true;
                    Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to export CSV: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
