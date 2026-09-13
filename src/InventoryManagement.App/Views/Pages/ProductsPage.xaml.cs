using System.Windows;
using System.Windows.Controls;
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
