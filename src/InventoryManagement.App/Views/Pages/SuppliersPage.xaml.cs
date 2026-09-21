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

        // Only List and Edit modes have a wrapping ScrollViewer that needs
        // this fallback. History mode's ListView already scrolls natively
        // via mouse wheel on its own (standard WPF behavior) - if we forced
        // routing here regardless of mode, History would incorrectly try to
        // scroll the hidden ListScrollViewer instead of doing nothing and
        // letting the ListView handle it itself.
        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_viewModel.IsViewingHistory)
        {
            return;
        }

        var scrollViewer = _viewModel.IsEditing ? EditScrollViewer : ListScrollViewer;
        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}
