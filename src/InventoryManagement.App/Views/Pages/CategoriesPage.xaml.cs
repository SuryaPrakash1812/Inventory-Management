using System.Windows.Controls;
using System.Windows.Input;
using InventoryManagement.App.ViewModels.Categories;

namespace InventoryManagement.App.Views.Pages;

public partial class CategoriesPage : Page
{
    private readonly CategoriesViewModel _viewModel;

    public CategoriesPage(CategoriesViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();

        DataContext = viewModel;

        Loaded += async (_, _) => await viewModel.InitializeAsync();

        // See ProductsPage for why this fallback exists: drives whichever
        // ScrollViewer is currently visible directly from the mouse wheel
        // rather than relying solely on automatic event bubbling.
        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scrollViewer = _viewModel.IsEditing ? EditScrollViewer : ListScrollViewer;
        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}
