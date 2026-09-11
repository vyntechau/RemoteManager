using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RemoteManager.Core.Logging;
using Wpf.Ui.Controls;
using TextBlock = Wpf.Ui.Controls.TextBlock;
using UiMessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace RemoteManager.App.Views;

/// <summary>
/// Fluent UI confirmation dialog provider using WPF-UI's native MessageBox component.
/// Provides Windows 11 Mica/Acrylic backdrops, Fluent buttons, and native Win32 window
/// layering to completely prevent RDP/VNC WinForms airspace occlusion.
/// </summary>
public static class ConfirmationDialog
{
    /// <summary>
    /// Displays a Fluent UI simple confirmation dialog (e.g. for closing tabs or disconnecting).
    /// </summary>
    public static async Task<bool> ShowSimpleAsync(
        Window? owner,
        string title,
        string message,
        string actionText = "Confirm",
        bool isDanger = true)
    {
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return await Application.Current.Dispatcher.InvokeAsync(() => ShowSimpleAsync(owner, title, message, actionText, isDanger)).Task.Unwrap();
        }

        LogEngine.Instance.Debug("UI", $"ConfirmationDialog.ShowSimpleAsync: title='{title}', action='{actionText}'");

        var parent = owner ?? Application.Current?.MainWindow;
        var uiMessageBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = message,
            PrimaryButtonText = actionText,
            PrimaryButtonAppearance = isDanger ? ControlAppearance.Danger : ControlAppearance.Primary,
            CloseButtonText = "Cancel",
            CloseButtonAppearance = ControlAppearance.Secondary,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        if (parent != null && parent.IsVisible)
        {
            uiMessageBox.Owner = parent;
        }
        else
        {
            uiMessageBox.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        var result = await uiMessageBox.ShowDialogAsync();
        return result == UiMessageBoxResult.Primary;
    }

    /// <summary>
    /// Displays a Fluent UI confirm-with-name dialog where the user must type the exact item name.
    /// </summary>
    public static async Task<bool> ShowConfirmWithNameAsync(
        Window? owner,
        string title,
        string message,
        string itemName,
        string actionText = "Delete")
    {
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return await Application.Current.Dispatcher.InvokeAsync(() => ShowConfirmWithNameAsync(owner, title, message, itemName, actionText)).Task.Unwrap();
        }

        LogEngine.Instance.Debug("UI", $"ConfirmationDialog.ShowConfirmWithNameAsync: title='{title}', itemName='{itemName}', action='{actionText}'");

        var parent = owner ?? Application.Current?.MainWindow;

        var contentPanel = new StackPanel
        {
            Margin = new Thickness(0, 4, 0, 8),
            MinWidth = 380,
            MaxWidth = 440
        };

        var messageBlock = new TextBlock
        {
            Text = message,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
            Foreground = Application.Current?.FindResource("TextFillColorSecondaryBrush") as Brush ?? Brushes.Gray
        };
        contentPanel.Children.Add(messageBlock);

        var promptBlock = new TextBlock
        {
            Text = "Type the name below to confirm:",
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = Application.Current?.FindResource("TextFillColorSecondaryBrush") as Brush ?? Brushes.Gray
        };
        contentPanel.Children.Add(promptBlock);

        // Name display with copy button inside a subtle card
        var nameGrid = new Grid();
        nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameBlock = new TextBlock
        {
            Text = itemName,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            FontFamily = new FontFamily("Consolas, Cascadia Code, monospace"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = Application.Current?.FindResource("TextFillColorPrimaryBrush") as Brush ?? Brushes.White
        };
        Grid.SetColumn(nameBlock, 0);
        nameGrid.Children.Add(nameBlock);

        var copyIcon = new SymbolIcon { Symbol = SymbolRegular.Copy24, FontSize = 14 };
        var copyBtn = new Wpf.Ui.Controls.Button
        {
            Icon = copyIcon,
            Appearance = ControlAppearance.Transparent,
            Height = 28,
            Width = 28,
            Padding = new Thickness(0),
            ToolTip = "Copy name to clipboard",
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        copyBtn.Click += (s, e) =>
        {
            try
            {
                Clipboard.SetText(itemName);
                copyIcon.Symbol = SymbolRegular.Checkmark24;
                copyIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x41));
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (ts, te) =>
                {
                    timer.Stop();
                    copyIcon.Symbol = SymbolRegular.Copy24;
                    copyIcon.Foreground = Application.Current?.FindResource("TextFillColorSecondaryBrush") as Brush ?? Brushes.Gray;
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                LogEngine.Instance.Error("UI", "Failed to copy item name to clipboard", ex);
            }
        };
        Grid.SetColumn(copyBtn, 1);
        nameGrid.Children.Add(copyBtn);

        var nameBorder = new Border
        {
            Background = Application.Current?.FindResource("SubtleFillColorSecondaryBrush") as Brush ?? new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)),
            BorderBrush = Application.Current?.FindResource("ControlElevationBorderBrush") as Brush ?? new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            Child = nameGrid
        };
        contentPanel.Children.Add(nameBorder);

        var inputBox = new Wpf.Ui.Controls.TextBox
        {
            PlaceholderText = "Type the name to confirm...",
            Margin = new Thickness(0, 10, 0, 0),
            Height = 36
        };
        contentPanel.Children.Add(inputBox);

        var uiMessageBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = contentPanel,
            PrimaryButtonText = actionText,
            PrimaryButtonAppearance = ControlAppearance.Danger,
            IsPrimaryButtonEnabled = false,
            CloseButtonText = "Cancel",
            CloseButtonAppearance = ControlAppearance.Secondary,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        if (parent != null && parent.IsVisible)
        {
            uiMessageBox.Owner = parent;
        }
        else
        {
            uiMessageBox.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        inputBox.TextChanged += (s, e) =>
        {
            var typed = inputBox.Text?.Trim() ?? string.Empty;
            uiMessageBox.IsPrimaryButtonEnabled = string.Equals(typed, itemName, StringComparison.Ordinal);
        };

        inputBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter && uiMessageBox.IsPrimaryButtonEnabled)
            {
                uiMessageBox.TemplateButtonCommand?.Execute(Wpf.Ui.Controls.MessageBoxButton.Primary);
                e.Handled = true;
            }
        };

        uiMessageBox.Loaded += (s, e) =>
        {
            inputBox.Focus();
        };

        var result = await uiMessageBox.ShowDialogAsync();
        return result == UiMessageBoxResult.Primary;
    }
}
