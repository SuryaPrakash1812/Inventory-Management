using System.Windows.Controls;
using InventoryManagement.App.ViewModels;

namespace InventoryManagement.App.Views.Pages;

/// <summary>
/// Placeholder page for Inventory. Live stock levels arrive in Stage 7 (Inventory and Stock Adjustments).
/// </summary>
public partial class InventoryPage : Page
{
    public InventoryPage()
    {
        InitializeComponent();

        DataContext = new PlaceholderViewModel("Inventory", "Live stock levels arrive in Stage 7 (Inventory and Stock Adjustments).");
    }
}
