using System.Windows;
using InventoryManagement.App.ViewModels.Licensing;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace InventoryManagement.App.Views.Licensing;

public partial class ActivationWindow : FluentWindow
{
    public ActivationViewModel ViewModel { get; }

    public ActivationWindow(ActivationViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        ApplicationThemeManager.Apply(this);

        viewModel.Activated += (_, _) => DialogResult = true;
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
