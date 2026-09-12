using System.Windows.Controls;
using InventoryManagement.App.ViewModels;

namespace InventoryManagement.App.Views.Pages;

/// <summary>
/// Placeholder page for Dashboard. Key metrics and quick actions will appear here starting in Stage 8 (Dashboard and Reports).
/// </summary>
public partial class DashboardPage : Page
{
    public DashboardPage()
    {
        InitializeComponent();

        DataContext = new PlaceholderViewModel("Dashboard", "Key metrics and quick actions will appear here starting in Stage 8 (Dashboard and Reports).");
    }
}
