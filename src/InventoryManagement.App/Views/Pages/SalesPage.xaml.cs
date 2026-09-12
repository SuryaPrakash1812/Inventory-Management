using System.Windows.Controls;
using InventoryManagement.App.ViewModels;

namespace InventoryManagement.App.Views.Pages;

/// <summary>
/// Placeholder page for Sales. Sales entry and stock-out logic arrive in Stage 6 (Sales).
/// </summary>
public partial class SalesPage : Page
{
    public SalesPage(ShellViewModel shell)
    {
        InitializeComponent();

        DataContext = new PlaceholderViewModel("Sales", "Sales entry and stock-out logic arrive in Stage 6 (Sales).");
        shell.CurrentPageTitle = "Sales";
    }
}
