using System.Windows.Controls;
using InventoryManagement.App.ViewModels;

namespace InventoryManagement.App.Views.Pages;

/// <summary>
/// Placeholder page for Products. Product catalog management arrives in Stage 3 (Products and Categories).
/// </summary>
public partial class ProductsPage : Page
{
    public ProductsPage(ShellViewModel shell)
    {
        InitializeComponent();

        DataContext = new PlaceholderViewModel("Products", "Product catalog management arrives in Stage 3 (Products and Categories).");
        shell.CurrentPageTitle = "Products";
    }
}
