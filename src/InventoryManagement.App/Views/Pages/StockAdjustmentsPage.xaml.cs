using System.Windows.Controls;
using InventoryManagement.App.ViewModels;

namespace InventoryManagement.App.Views.Pages;

/// <summary>
/// Placeholder page for Stock Adjustments. Manual stock adjustments arrive in Stage 7 (Inventory and Stock Adjustments).
/// </summary>
public partial class StockAdjustmentsPage : Page
{
    public StockAdjustmentsPage(ShellViewModel shell)
    {
        InitializeComponent();

        DataContext = new PlaceholderViewModel("Stock Adjustments", "Manual stock adjustments arrive in Stage 7 (Inventory and Stock Adjustments).");
        shell.CurrentPageTitle = "Stock Adjustments";
    }
}
