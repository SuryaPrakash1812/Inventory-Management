using System.Windows.Controls;
using System.Windows.Input;
using InventoryManagement.App.ViewModels.Dashboard;

namespace InventoryManagement.App.Views.Pages;

public partial class DashboardPage : Page
{
    private readonly DashboardViewModel _viewModel;

    public DashboardPage(DashboardViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();

        DataContext = viewModel;

        Loaded += async (_, _) => await viewModel.InitializeAsync();

        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        DashboardScrollViewer.ScrollToVerticalOffset(DashboardScrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}
