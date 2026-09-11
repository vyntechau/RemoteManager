using System.Windows;
using System.Windows.Controls;
using RemoteManager.Core.Interfaces;
using RemoteManager.Core.Models;

namespace RemoteManager.App.Views;

public partial class CredentialEditView : UserControl
{
    public Credential? Existing { get; private set; }
    public Credential? ResultCredential { get; private set; }

    public event Action<Credential>? CredentialSaved;
    public event Action? CancelRequested;

    private IEncryptionService? _encryptionService;
    private bool _isPasswordRevealed = false;
    private bool _isInitialized = false;

    public CredentialEditView()
    {
        InitializeComponent();
        _isInitialized = true;
    }

    public void LoadCredential(Credential? existing, IEncryptionService encryptionService)
    {
        Existing = existing;
        ResultCredential = null;
        _encryptionService = encryptionService;
        _isPasswordRevealed = false;

        if (TitleErrorText != null) TitleErrorText.Visibility = Visibility.Collapsed;
        if (UsernameErrorText != null) UsernameErrorText.Visibility = Visibility.Collapsed;

        if (PasswordVisibleInput != null) PasswordVisibleInput.Visibility = Visibility.Collapsed;
        if (PasswordInput != null) PasswordInput.Visibility = Visibility.Visible;

        if (existing != null)
        {
            if (PageTitleText != null) PageTitleText.Text = $"Edit Credential: {existing.Title}";
            if (TitleInput != null) TitleInput.Text = existing.Title;
            if (UsernameInput != null) UsernameInput.Text = existing.Username;
            if (DomainInput != null) DomainInput.Text = existing.Domain ?? string.Empty;
            if (NotesInput != null) NotesInput.Text = existing.Notes ?? string.Empty;

            var decrypted = string.Empty;
            if (!string.IsNullOrEmpty(existing.EncryptedPassword))
            {
                try
                {
                    decrypted = _encryptionService.Decrypt(existing.EncryptedPassword);
                }
                catch
                {
                    decrypted = string.Empty;
                }
            }

            if (PasswordInput != null) PasswordInput.Password = decrypted;
            if (PasswordVisibleInput != null) PasswordVisibleInput.Text = decrypted;
        }
        else
        {
            if (PageTitleText != null) PageTitleText.Text = "New Vault Credential";
            if (TitleInput != null) TitleInput.Text = string.Empty;
            if (UsernameInput != null) UsernameInput.Text = string.Empty;
            if (DomainInput != null) DomainInput.Text = string.Empty;
            if (PasswordInput != null) PasswordInput.Password = string.Empty;
            if (PasswordVisibleInput != null) PasswordVisibleInput.Text = string.Empty;
            if (NotesInput != null) NotesInput.Text = string.Empty;
        }
    }

    private void OnFormTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized) return;

        if (TitleInput != null && !string.IsNullOrWhiteSpace(TitleInput.Text))
        {
            if (TitleErrorText != null) TitleErrorText.Visibility = Visibility.Collapsed;
        }
        if (UsernameInput != null && !string.IsNullOrWhiteSpace(UsernameInput.Text))
        {
            if (UsernameErrorText != null) UsernameErrorText.Visibility = Visibility.Collapsed;
        }
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        if (!_isPasswordRevealed && PasswordVisibleInput != null && PasswordInput != null)
        {
            PasswordVisibleInput.Text = PasswordInput.Password;
        }
    }

    private void OnVisiblePasswordChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized) return;
        if (_isPasswordRevealed && PasswordInput != null && PasswordVisibleInput != null)
        {
            PasswordInput.Password = PasswordVisibleInput.Text;
        }
    }

    private void OnTogglePasswordVisibility(object sender, RoutedEventArgs e)
    {
        _isPasswordRevealed = !_isPasswordRevealed;
        if (_isPasswordRevealed)
        {
            if (PasswordVisibleInput != null && PasswordInput != null)
            {
                PasswordVisibleInput.Text = PasswordInput.Password;
                PasswordInput.Visibility = Visibility.Collapsed;
                PasswordVisibleInput.Visibility = Visibility.Visible;
                PasswordVisibleInput.Focus();
            }
            if (PasswordEyeIcon != null) PasswordEyeIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.EyeOff24;
        }
        else
        {
            if (PasswordInput != null && PasswordVisibleInput != null)
            {
                PasswordInput.Password = PasswordVisibleInput.Text;
                PasswordVisibleInput.Visibility = Visibility.Collapsed;
                PasswordInput.Visibility = Visibility.Visible;
                PasswordInput.Focus();
            }
            if (PasswordEyeIcon != null) PasswordEyeIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Eye24;
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var isValid = true;
        if (TitleInput == null || string.IsNullOrWhiteSpace(TitleInput.Text))
        {
            if (TitleErrorText != null) TitleErrorText.Visibility = Visibility.Visible;
            isValid = false;
        }

        if (UsernameInput == null || string.IsNullOrWhiteSpace(UsernameInput.Text))
        {
            if (UsernameErrorText != null) UsernameErrorText.Visibility = Visibility.Visible;
            isValid = false;
        }

        if (!isValid) return;

        var rawPassword = _isPasswordRevealed ? PasswordVisibleInput?.Text ?? string.Empty : PasswordInput?.Password ?? string.Empty;
        var encryptedPassword = string.Empty;

        if (_encryptionService != null && !string.IsNullOrEmpty(rawPassword))
        {
            encryptedPassword = _encryptionService.Encrypt(rawPassword);
        }

        ResultCredential = new Credential
        {
            Id = Existing?.Id ?? Guid.NewGuid(),
            Title = TitleInput!.Text.Trim(),
            Username = UsernameInput!.Text.Trim(),
            Domain = string.IsNullOrWhiteSpace(DomainInput?.Text) ? null : DomainInput.Text.Trim(),
            EncryptedPassword = encryptedPassword,
            Notes = string.IsNullOrWhiteSpace(NotesInput?.Text) ? null : NotesInput.Text.Trim(),
            CreatedAt = Existing?.CreatedAt ?? DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        CredentialSaved?.Invoke(ResultCredential);
    }
}
