using System.Windows.Controls;
using InventoryManagement.App.ViewModels;

namespace InventoryManagement.App.Views.Pages;

/// <summary>
/// Placeholder page for Customers. Customer management arrives in Stage 4 (Suppliers and Customers).
/// </summary>
public partial class CustomersPage : Page
{
    public CustomersPage(ShellViewModel shell)
    {
        InitializeComponent();

        DataContext = new PlaceholderViewModel("Customers", "Customer management arrives in Stage 4 (Suppliers and Customers).");
        shell.CurrentPageTitle = "Customers";
    }
}
