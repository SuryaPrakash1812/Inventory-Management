using System.Windows.Controls;
using System.Windows.Input;
using InventoryManagement.App.ViewModels.Customers;

namespace InventoryManagement.App.Views.Pages;

public partial class CustomersPage : Page
{
    private readonly CustomersViewModel _viewModel;

    public CustomersPage(CustomersViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();

        DataContext = viewModel;

        Loaded += async (_, _) => await viewModel.InitializeAsync();

        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_viewModel.IsEditing)
        {
            return;
        }

        EditScrollViewer.ScrollToVerticalOffset(EditScrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}
