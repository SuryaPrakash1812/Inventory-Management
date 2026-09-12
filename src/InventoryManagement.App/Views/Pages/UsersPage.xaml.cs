using System.Windows.Controls;
using InventoryManagement.App.ViewModels.Users;

namespace InventoryManagement.App.Views.Pages;

public partial class UsersPage : Page
{
    public UsersPage(UsersViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;

        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
