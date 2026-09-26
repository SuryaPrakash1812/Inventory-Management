using System.Windows.Controls;
using System.Windows.Input;
using InventoryManagement.App.ViewModels.Backup;

namespace InventoryManagement.App.Views.Pages;

public partial class BackupPage : Page
{
    private readonly BackupViewModel _viewModel;

    public BackupPage(BackupViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();

        DataContext = viewModel;

        Loaded += async (_, _) => await viewModel.InitializeAsync();

        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        BackupScrollViewer.ScrollToVerticalOffset(BackupScrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}
