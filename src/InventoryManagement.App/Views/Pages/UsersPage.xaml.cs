using System.Windows.Controls;
using InventoryManagement.App.ViewModels;
using InventoryManagement.App.ViewModels.Users;

namespace InventoryManagement.App.Views.Pages;

public partial class UsersPage : Page
{
    public UsersPage(UsersViewModel viewModel, ShellViewModel shell)
    {
        InitializeComponent();

        DataContext = viewModel;
        shell.CurrentPageTitle = "Users";

        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
