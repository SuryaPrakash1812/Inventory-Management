using System.Windows.Controls;
using System.Windows.Input;
using InventoryManagement.App.ViewModels.Reports;

namespace InventoryManagement.App.Views.Pages;

public partial class ReportsPage : Page
{
    private readonly ReportsViewModel _viewModel;

    public ReportsPage(ReportsViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();

        DataContext = viewModel;

        Loaded += async (_, _) => await viewModel.InitializeAsync();

        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ReportsScrollViewer.ScrollToVerticalOffset(ReportsScrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}
