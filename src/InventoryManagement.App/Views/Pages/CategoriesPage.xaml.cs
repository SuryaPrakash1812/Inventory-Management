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

        // See ProductsPage for why this fallback exists: drives the
        // ScrollViewer directly from the mouse wheel rather than relying
        // solely on automatic event bubbling. Only active while the edit
        // form is showing, so it never interferes with the category grid's
        // own scrolling in list view.
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
