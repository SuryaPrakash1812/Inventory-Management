using System.Windows.Controls;
using System.Windows.Input;
using InventoryManagement.App.ViewModels.Inventory;

namespace InventoryManagement.App.Views.Pages;

public partial class InventoryPage : Page
{
    private readonly InventoryViewModel _viewModel;

    public InventoryPage(InventoryViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();

        DataContext = viewModel;

        Loaded += async (_, _) => await viewModel.InitializeAsync();

        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scrollViewer = _viewModel.IsViewingHistory ? HistoryScrollViewer : OverviewScrollViewer;
        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}
