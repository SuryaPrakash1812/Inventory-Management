using System.Windows.Controls;
using InventoryManagement.App.ViewModels;

namespace InventoryManagement.App.Views.Pages;

/// <summary>
/// Placeholder page for Backup. Local and cloud backup/restore arrive in Stages 11-12 (Backup engine and Cloud providers).
/// </summary>
public partial class BackupPage : Page
{
    public BackupPage()
    {
        InitializeComponent();

        DataContext = new PlaceholderViewModel("Backup", "Local and cloud backup/restore arrive in Stages 11-12 (Backup engine and Cloud providers).");
    }
}
