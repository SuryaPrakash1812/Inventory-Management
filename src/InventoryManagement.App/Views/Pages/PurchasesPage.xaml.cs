using System.Windows.Controls;
using InventoryManagement.App.ViewModels;

namespace InventoryManagement.App.Views.Pages;

/// <summary>
/// Placeholder page for Purchases. Purchase entry and stock-in logic arrive in Stage 5 (Purchases).
/// </summary>
public partial class PurchasesPage : Page
{
    public PurchasesPage()
    {
        InitializeComponent();

        DataContext = new PlaceholderViewModel("Purchases", "Purchase entry and stock-in logic arrive in Stage 5 (Purchases).");
    }
}
