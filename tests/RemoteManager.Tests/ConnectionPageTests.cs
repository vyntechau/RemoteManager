using RemoteManager.App.ViewModels;
using RemoteManager.Core.Models;
using RemoteManager.Data.Security;
using RemoteManager.Data;
using Xunit;

namespace RemoteManager.Tests;

public class ConnectionPageTests
{
    [Fact]
    public void VerifyWpfUiSymbols()
    {
        var names = Enum.GetNames(typeof(Wpf.Ui.Controls.SymbolRegular)).ToHashSet();
        var symbolsToVerify = new[]
        {
            "DocumentBulletList24", "ArrowDown24", "Copy24", "Delete24",
            "ArrowDownload24", "FolderOpen24", "ServerMultiple20", "Pulse24",
            "Flash24", "Grid24", "Table24", "Key24", "Edit24",
            "ShieldCheckmark24", "LockClosed24", "Server24", "Search24",
            "Navigation24", "PanelLeft24", "ChevronLeft24", "ChevronRight24", "LineHorizontal320"
        };

        foreach (var symbol in symbolsToVerify)
        {
            Assert.True(names.Contains(symbol), $"Symbol '{symbol}' should exist in Wpf.Ui.Controls.SymbolRegular");
        }

        var prop = typeof(Wpf.Ui.Controls.TextBox).GetProperty("Icon");
        Assert.NotNull(prop);
        Assert.Equal("IconElement", prop.PropertyType.Name);
    }

    [Fact]
    public void VerifyWpfUiMessageBox_CanBeInstantiated()
    {
        var thread = new System.Threading.Thread(() =>
        {
            var mb = new Wpf.Ui.Controls.MessageBox
            {
                Title = "Close Connection",
                Content = "Disconnect?",
                PrimaryButtonText = "Disconnect",
                PrimaryButtonAppearance = Wpf.Ui.Controls.ControlAppearance.Danger,
                CloseButtonText = "Cancel",
                CloseButtonAppearance = Wpf.Ui.Controls.ControlAppearance.Secondary
            };

            Assert.Equal("Close Connection", mb.Title);
            Assert.Equal("Disconnect?", mb.Content);
            Assert.Equal("Disconnect", mb.PrimaryButtonText);
            Assert.Equal(Wpf.Ui.Controls.ControlAppearance.Danger, mb.PrimaryButtonAppearance);
            Assert.Equal("Cancel", mb.CloseButtonText);
            Assert.True(mb.IsPrimaryButtonEnabled);
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void VerifyWpfUiMessageBox_ConfirmWithNameFlow()
    {
        var thread = new System.Threading.Thread(() =>
        {
            var panel = new System.Windows.Controls.StackPanel();
            var box = new Wpf.Ui.Controls.TextBox();
            panel.Children.Add(box);

            var mb = new Wpf.Ui.Controls.MessageBox
            {
                Title = "Delete Connection",
                Content = panel,
                PrimaryButtonText = "Delete",
                PrimaryButtonAppearance = Wpf.Ui.Controls.ControlAppearance.Danger,
                IsPrimaryButtonEnabled = false,
                CloseButtonText = "Cancel"
            };

            box.TextChanged += (s, e) =>
            {
                mb.IsPrimaryButtonEnabled = string.Equals(box.Text?.Trim(), "Server 1", StringComparison.Ordinal);
            };

            Assert.False(mb.IsPrimaryButtonEnabled);
            box.Text = "Wrong Name";
            Assert.False(mb.IsPrimaryButtonEnabled);
            box.Text = "Server 1";
            Assert.True(mb.IsPrimaryButtonEnabled);
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void ConnectionCardViewModel_PresentationProperties_MatchExpectedValues()
    {
        var rdpConn = new ConnectionItem
        {
            Name = "Prod Windows Server",
            Host = "10.0.0.5",
            Port = 3389,
            Protocol = ProtocolType.RDP,
            DisplayMode = DisplayMode.Tabbed
        };

        var card = new ConnectionCardViewModel(rdpConn, "Vault-Prod-Admin");

        Assert.Equal("Prod Windows Server", card.DisplayName);
        Assert.Equal("10.0.0.5:3389", card.FullAddress);
        Assert.Equal("#0078D4", card.ProtocolBadgeColor);
        Assert.Equal("Embedded Tab", card.DisplayModeText);
        Assert.Equal("Vault-Prod-Admin", card.CredentialTitle);
        Assert.Equal(ConnectionPingStatus.Untested, card.PingStatus);
        Assert.Equal("Untested", card.PingStatusText);
        Assert.Equal("#8A8886", card.PingStatusColor);
    }

    [Fact]
    public void ConnectionCardViewModel_ProtocolColors_AreAccurateForAllProtocols()
    {
        var rdp = new ConnectionCardViewModel(new ConnectionItem { Protocol = ProtocolType.RDP });
        var ssh = new ConnectionCardViewModel(new ConnectionItem { Protocol = ProtocolType.SSH });
        var vnc = new ConnectionCardViewModel(new ConnectionItem { Protocol = ProtocolType.VNC });
        var web = new ConnectionCardViewModel(new ConnectionItem { Protocol = ProtocolType.Web });

        Assert.Equal("#0078D4", rdp.ProtocolBadgeColor);
        Assert.Equal("#107C41", ssh.ProtocolBadgeColor);
        Assert.Equal("#D83B01", vnc.ProtocolBadgeColor);
        Assert.Equal("#881798", web.ProtocolBadgeColor);
    }

    [Fact]
    public void ConnectionItem_Clone_CreatesIndependentCopy()
    {
        var original = new ConnectionItem
        {
            Name = "Database Master",
            Host = "192.168.1.10",
            Port = 22,
            Protocol = ProtocolType.SSH,
            DisplayMode = DisplayMode.Tabbed,
            CredentialId = Guid.NewGuid()
        };

        var clone = original.Clone();

        Assert.NotEqual(original.Id, clone.Id);
        Assert.Equal("Database Master (Copy)", clone.Name);
        Assert.Equal(original.Host, clone.Host);
        Assert.Equal(original.Port, clone.Port);
        Assert.Equal(original.Protocol, clone.Protocol);
        Assert.Equal(original.DisplayMode, clone.DisplayMode);
        Assert.Equal(original.CredentialId, clone.CredentialId);
    }

    [Fact]
    public void ConnectionsViewModel_SyncAndMetrics_CalculateAccurately()
    {
        var db = new SqliteDatabaseService(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db"));
        var crypto = new DpapiEncryptionService();
        var mainVm = new MainViewModel(db, crypto);
        var vm = new ConnectionsViewModel(mainVm);

        var list = new List<ConnectionItem>
        {
            new() { Name = "RDP 1", Host = "10.0.0.1", Port = 3389, Protocol = ProtocolType.RDP },
            new() { Name = "RDP 2", Host = "10.0.0.2", Port = 3389, Protocol = ProtocolType.RDP },
            new() { Name = "SSH 1", Host = "10.0.0.3", Port = 22, Protocol = ProtocolType.SSH },
            new() { Name = "VNC 1", Host = "10.0.0.4", Port = 5900, Protocol = ProtocolType.VNC },
            new() { Name = "Web 1", Host = "https://admin.portal.local", Port = 443, Protocol = ProtocolType.Web }
        };

        vm.SyncConnections(list, []);

        Assert.Equal(5, vm.TotalCount);
        Assert.Equal(2, vm.RdpCount);
        Assert.Equal(1, vm.SshCount);
        Assert.Equal(1, vm.VncCount);
        Assert.Equal(1, vm.WebCount);
        Assert.Equal(5, vm.FilteredCards.Count);
    }

    [Fact]
    public void ConnectionsViewModel_FilteringByProtocol_FiltersCorrectly()
    {
        var db = new SqliteDatabaseService(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db"));
        var crypto = new DpapiEncryptionService();
        var mainVm = new MainViewModel(db, crypto);
        var vm = new ConnectionsViewModel(mainVm);

        var list = new List<ConnectionItem>
        {
            new() { Name = "Windows DC", Host = "10.0.0.1", Port = 3389, Protocol = ProtocolType.RDP },
            new() { Name = "Linux Bastion", Host = "10.0.0.2", Port = 22, Protocol = ProtocolType.SSH },
            new() { Name = "Mac Mini VNC", Host = "10.0.0.3", Port = 5900, Protocol = ProtocolType.VNC }
        };

        vm.SyncConnections(list, []);

        // Filter to RDP
        vm.SetProtocolFilter("RDP");
        Assert.Single(vm.FilteredCards);
        Assert.Equal("Windows DC", vm.FilteredCards[0].DisplayName);

        // Filter to SSH
        vm.SetProtocolFilter("SSH");
        Assert.Single(vm.FilteredCards);
        Assert.Equal("Linux Bastion", vm.FilteredCards[0].DisplayName);

        // Filter to VNC
        vm.SetProtocolFilter("VNC");
        Assert.Single(vm.FilteredCards);
        Assert.Equal("Mac Mini VNC", vm.FilteredCards[0].DisplayName);

        // Reset to All
        vm.SetProtocolFilter("All");
        Assert.Equal(3, vm.FilteredCards.Count);
    }

    [Fact]
    public void ConnectionsViewModel_SearchFiltering_MatchesNameOrHost()
    {
        var db = new SqliteDatabaseService(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db"));
        var crypto = new DpapiEncryptionService();
        var mainVm = new MainViewModel(db, crypto);
        var vm = new ConnectionsViewModel(mainVm);

        var list = new List<ConnectionItem>
        {
            new() { Name = "Alpha Server", Host = "192.168.1.50", Port = 3389, Protocol = ProtocolType.RDP },
            new() { Name = "Beta Node", Host = "192.168.1.60", Port = 22, Protocol = ProtocolType.SSH },
            new() { Name = "Gamma Cluster", Host = "10.200.0.1", Port = 5900, Protocol = ProtocolType.VNC }
        };

        vm.SyncConnections(list, []);

        vm.SearchText = "beta";
        Assert.Single(vm.FilteredCards);
        Assert.Equal("Beta Node", vm.FilteredCards[0].DisplayName);

        vm.SearchText = "10.200";
        Assert.Single(vm.FilteredCards);
        Assert.Equal("Gamma Cluster", vm.FilteredCards[0].DisplayName);

        vm.ClearSearch();
        Assert.Equal(3, vm.FilteredCards.Count);
    }

    [Fact]
    public void ConnectionsViewModel_Sorting_OrdersResultsCorrectly()
    {
        var db = new SqliteDatabaseService(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db"));
        var crypto = new DpapiEncryptionService();
        var mainVm = new MainViewModel(db, crypto);
        var vm = new ConnectionsViewModel(mainVm);

        var list = new List<ConnectionItem>
        {
            new() { Name = "Charlie", Host = "10.0.0.3", Port = 3389, Protocol = ProtocolType.RDP },
            new() { Name = "Alice", Host = "10.0.0.1", Port = 22, Protocol = ProtocolType.SSH },
            new() { Name = "Bob", Host = "10.0.0.2", Port = 5900, Protocol = ProtocolType.VNC }
        };

        vm.SyncConnections(list, []);

        vm.SelectedSortOption = "Name (A-Z)";
        Assert.Equal("Alice", vm.FilteredCards[0].DisplayName);
        Assert.Equal("Bob", vm.FilteredCards[1].DisplayName);
        Assert.Equal("Charlie", vm.FilteredCards[2].DisplayName);

        vm.SelectedSortOption = "Name (Z-A)";
        Assert.Equal("Charlie", vm.FilteredCards[0].DisplayName);
        Assert.Equal("Bob", vm.FilteredCards[1].DisplayName);
        Assert.Equal("Alice", vm.FilteredCards[2].DisplayName);
    }

    [Fact]
    public void MainViewModel_SearchText_SynchronizesWithConnectionsVM_AndFiltersResults()
    {
        var db = new SqliteDatabaseService(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db"));
        var crypto = new DpapiEncryptionService();
        var mainVm = new MainViewModel(db, crypto);

        var list = new List<ConnectionItem>
        {
            new() { Name = "Web Frontend", Host = "10.0.0.10", Port = 443, Protocol = ProtocolType.Web },
            new() { Name = "Database Replica", Host = "10.0.0.20", Port = 22, Protocol = ProtocolType.SSH }
        };

        mainVm.Connections = new System.Collections.ObjectModel.ObservableCollection<ConnectionItem>(list);
        mainVm.ConnectionsVM.SyncConnections(list, []);

        // Search from MainViewModel (top bar)
        mainVm.SearchText = "Replica";

        Assert.Equal("Replica", mainVm.ConnectionsVM.SearchText);
        Assert.Single(mainVm.ConnectionsVM.FilteredCards);
        Assert.Equal("Database Replica", mainVm.ConnectionsVM.FilteredCards[0].DisplayName);
        Assert.Single(mainVm.FilteredConnections);

        // Search from ConnectionsVM (in-page bar)
        mainVm.ConnectionsVM.SearchText = "Frontend";

        Assert.Equal("Frontend", mainVm.SearchText);
        Assert.Single(mainVm.ConnectionsVM.FilteredCards);
        Assert.Equal("Web Frontend", mainVm.ConnectionsVM.FilteredCards[0].DisplayName);
        Assert.Single(mainVm.FilteredConnections);
    }
}
