using System.Windows.Controls;
using InventoryManagement.App.ViewModels;

namespace InventoryManagement.App.Views.Pages;

/// <summary>
/// Placeholder page for Categories. Category management arrives in Stage 3 (Products and Categories).
/// </summary>
public partial class CategoriesPage : Page
{
    public CategoriesPage()
    {
        InitializeComponent();

        DataContext = new PlaceholderViewModel("Categories", "Category management arrives in Stage 3 (Products and Categories).");
    }
}
