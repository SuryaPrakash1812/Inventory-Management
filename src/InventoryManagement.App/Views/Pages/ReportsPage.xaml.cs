using System.Windows.Controls;
using InventoryManagement.App.ViewModels;

namespace InventoryManagement.App.Views.Pages;

/// <summary>
/// Placeholder page for Reports. Reporting arrives in Stage 8 (Dashboard and Reports).
/// </summary>
public partial class ReportsPage : Page
{
    public ReportsPage()
    {
        InitializeComponent();

        DataContext = new PlaceholderViewModel("Reports", "Reporting arrives in Stage 8 (Dashboard and Reports).");
    }
}
