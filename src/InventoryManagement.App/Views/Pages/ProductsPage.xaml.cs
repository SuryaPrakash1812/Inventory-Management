using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InventoryManagement.App.ViewModels.Products;
using Microsoft.Win32;

namespace InventoryManagement.App.Views.Pages;

public partial class ProductsPage : Page
{
    private readonly ProductsViewModel _viewModel;

    public ProductsPage(ProductsViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();

        DataContext = viewModel;

        Loaded += async (_, _) => await viewModel.InitializeAsync();

        // Belt-and-braces fallback: drive whichever ScrollViewer is
        // currently visible directly from the mouse wheel, rather than
        // relying solely on WPF's automatic event bubbling (which depends
        // on every element between the cursor and the ScrollViewer being
        // hit-testable - easy to get subtly wrong with custom styled
        // controls, and the DataGrid's own internal scrolling only covers
        // its rows, not the filter row(s) or pagination controls above/
        // below it if the whole page is taller than the window).
        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scrollViewer = _viewModel.IsEditing ? EditScrollViewer : ListScrollViewer;
        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    /// <summary>
    /// Selects all text when a numeric field gets focus, so a field showing
    /// its default "0" can just be typed over immediately instead of
    /// requiring the user to manually clear it first.
    /// </summary>
    private void OnNumericFieldGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.SelectAll();
        }
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Products",
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"products-{DateTime.Now:yyyy-MM-dd}.csv",
        };

        if (dialog.ShowDialog() == true)
        {
            await _viewModel.ExportToFileAsync(dialog.FileName);
        }
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Products",
            Filter = "CSV files (*.csv)|*.csv",
        };

        if (dialog.ShowDialog() == true)
        {
            await _viewModel.ImportFromFileAsync(dialog.FileName);
        }
    }
}
