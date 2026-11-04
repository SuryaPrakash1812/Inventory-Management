using System.Windows.Controls;
using System.Windows.Input;
using InventoryManagement.App.ViewModels.Suppliers;

namespace InventoryManagement.App.Views.Pages;

public partial class SuppliersPage : Page
{
    private readonly SuppliersViewModel _viewModel;

    public SuppliersPage(SuppliersViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();

        DataContext = viewModel;

        Loaded += async (_, _) => await viewModel.InitializeAsync();

        // See ProductsPage: drives whichever view's ScrollViewer is visible
        // directly from the mouse wheel.
        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scrollViewer = _viewModel.IsViewingHistory ? HistoryScrollViewer
            : _viewModel.IsEditing ? EditScrollViewer
            : ListScrollViewer;
        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}
