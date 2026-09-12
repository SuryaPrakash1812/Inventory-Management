using System.Windows.Controls;
using InventoryManagement.App.ViewModels;

namespace InventoryManagement.App.Views.Pages;

/// <summary>
/// Placeholder page for Users. User accounts, authentication and permissions arrive in Stage 9 (Users, Auth, Permissions).
/// </summary>
public partial class UsersPage : Page
{
    public UsersPage(ShellViewModel shell)
    {
        InitializeComponent();

        DataContext = new PlaceholderViewModel("Users", "User accounts, authentication and permissions arrive in Stage 9 (Users, Auth, Permissions).");
        shell.CurrentPageTitle = "Users";
    }
}
