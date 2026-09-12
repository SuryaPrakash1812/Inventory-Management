using System.Windows.Controls;
using InventoryManagement.App.ViewModels;

namespace InventoryManagement.App.Views.Pages;

/// <summary>
/// Placeholder page for Suppliers. Supplier management arrives in Stage 4 (Suppliers and Customers).
/// </summary>
public partial class SuppliersPage : Page
{
    public SuppliersPage(ShellViewModel shell)
    {
        InitializeComponent();

        DataContext = new PlaceholderViewModel("Suppliers", "Supplier management arrives in Stage 4 (Suppliers and Customers).");
        shell.CurrentPageTitle = "Suppliers";
    }
}
