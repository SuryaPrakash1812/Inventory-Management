using InventoryManagement.App.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace InventoryManagement.App.Views;

/// <summary>
/// Code-behind is intentionally minimal: set the DataContext and apply the
/// WPF-UI theme, nothing else. All behaviour lives in <see cref="MainViewModel"/>.
/// </summary>
public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;

        ApplicationThemeManager.Apply(this);
    }
}
