using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RemoteManager.App.ViewModels;
using RemoteManager.Core.Models;

namespace RemoteManager.App.Views;

public partial class ConnectionsView : UserControl
{
    public ConnectionsView()
    {
        InitializeComponent();
    }

    private void OnQuickProtocolChanged(object sender, SelectionChangedEventArgs e)
    {
        var combo = sender as ComboBox ?? QuickPageProtocolCombo;
        if (combo != null &&
            DataContext is ConnectionsViewModel vm &&
            combo.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            Enum.TryParse<ProtocolType>(tag, true, out var proto))
        {
            vm.QuickProtocol = proto;
        }
    }

    private void OnCardMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            // Find parent Border with ContextMenu
            DependencyObject current = button;
            while (current != null && current is not Border)
            {
                current = VisualTreeHelper.GetParent(current);
            }

            if (current is Border border && border.ContextMenu != null)
            {
                border.ContextMenu.PlacementTarget = border;
                border.ContextMenu.IsOpen = true;
            }
        }
    }

    private void OnTableItemDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is ListView listView && listView.SelectedItem is ConnectionCardViewModel card && DataContext is ConnectionsViewModel vm)
        {
            _ = vm.ConnectCardAsync(card);
        }
    }

    private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        GridViewColumnHeader? header = e.OriginalSource as GridViewColumnHeader;
        if (header == null && e.OriginalSource is DependencyObject dep)
        {
            DependencyObject? curr = dep;
            while (curr != null && curr is not GridViewColumnHeader)
            {
                curr = VisualTreeHelper.GetParent(curr);
            }
            header = curr as GridViewColumnHeader;
        }

        if (header?.Column == null) return;

        string? columnKey = null;
        if (header.Column.Header is FrameworkElement fe && fe.Tag is string tag)
        {
            columnKey = tag;
        }
        else if (header.Tag is string headerTag)
        {
            columnKey = headerTag;
        }
        else if (sender is ListView lv && lv.View is GridView gv)
        {
            int colIdx = gv.Columns.IndexOf(header.Column);
            columnKey = colIdx switch
            {
                0 => "Bookmark",
                1 => "Status",
                2 => "Protocol",
                3 => "DisplayName",
                4 => "Endpoint",
                5 => "Credential",
                6 => "DisplayMode",
                _ => null
            };
        }
        else if (header.Column.Header is string str)
        {
            columnKey = str switch
            {
                var s when s.StartsWith("★") => "Bookmark",
                var s when s.StartsWith("Status") => "Status",
                var s when s.StartsWith("Protocol") => "Protocol",
                var s when s.StartsWith("Server Name") => "DisplayName",
                var s when s.StartsWith("Endpoint") || s.StartsWith("Host") => "Endpoint",
                var s when s.StartsWith("Credential") => "Credential",
                var s when s.StartsWith("Display Mode") => "DisplayMode",
                _ => null
            };
        }

        if (string.Equals(columnKey, "Actions", StringComparison.OrdinalIgnoreCase)) return;

        if (!string.IsNullOrEmpty(columnKey) && DataContext is ConnectionsViewModel vm)
        {
            vm.SortByColumn(columnKey);
        }
    }
}
