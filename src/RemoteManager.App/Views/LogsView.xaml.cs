using System.Windows.Controls;
using RemoteManager.App.ViewModels;
using RemoteManager.Core.Logging;

namespace RemoteManager.App.Views;

public partial class LogsView : UserControl
{
    private readonly LogsViewModel _viewModel;

    public LogsViewModel ViewModel => _viewModel;

    public LogsView()
    {
        InitializeComponent();

        _viewModel = new LogsViewModel(LogEngine.Instance);
        DataContext = _viewModel;

        _viewModel.RequestScrollToEnd = () =>
        {
            if (LogListView != null && _viewModel?.FilteredLogs != null && _viewModel.FilteredLogs.Count > 0)
            {
                var last = _viewModel.FilteredLogs[^1];
                LogListView.ScrollIntoView(last);
            }
        };

        Loaded += (s, e) =>
        {
            _viewModel?.RefreshAvailableFiles();
            LogEngine.Instance.Debug("UI", "LogsView loaded and active.");
        };
    }

    private void OnToolbarSizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
        if (ActionsPanel == null || ToolbarSecondRowDef == null) return;

        // When width is >= 1180px, layout is single row with actions aligned right.
        // Below 1180px, actions wrap to row 1, and inside each row items wrap as needed on very narrow screens.
        bool isWide = e.NewSize.Width >= 1180;

        if (isWide)
        {
            if (Grid.GetRow(ActionsPanel) != 0 || Grid.GetColumn(ActionsPanel) != 1)
            {
                Grid.SetRow(ActionsPanel, 0);
                Grid.SetColumn(ActionsPanel, 1);
                Grid.SetColumnSpan(ActionsPanel, 1);
                ActionsPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
                ToolbarSecondRowDef.Height = new System.Windows.GridLength(0);
            }
        }
        else
        {
            if (Grid.GetRow(ActionsPanel) != 1 || Grid.GetColumn(ActionsPanel) != 0)
            {
                Grid.SetRow(ActionsPanel, 1);
                Grid.SetColumn(ActionsPanel, 0);
                Grid.SetColumnSpan(ActionsPanel, 2);
                ActionsPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                ToolbarSecondRowDef.Height = System.Windows.GridLength.Auto;
            }
        }
    }
}
