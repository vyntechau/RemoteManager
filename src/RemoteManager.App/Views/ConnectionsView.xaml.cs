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
                border.ContextMenu.PlacementTarget = button;
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
}
