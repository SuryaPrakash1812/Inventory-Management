using System.Windows.Controls;
using InventoryManagement.App.ViewModels.Categories;

namespace InventoryManagement.App.Views.Pages;

public partial class CategoriesPage : Page
{
    public CategoriesPage(CategoriesViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;

        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
