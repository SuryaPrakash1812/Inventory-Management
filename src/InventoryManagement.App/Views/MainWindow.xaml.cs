using InventoryManagement.App.ViewModels;
using InventoryManagement.App.Views.Pages;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace InventoryManagement.App.Views;

/// <summary>
/// The application shell: title bar, sidebar navigation, header (current page
/// title + theme toggle + user), content frame, and status bar. Code-behind
/// only wires the DataContext and the WPF-UI navigation plumbing - everything
/// else lives in <see cref="ShellViewModel"/>.
/// </summary>
public partial class MainWindow : FluentWindow
{
    public ShellViewModel ViewModel { get; }

    public MainWindow(ShellViewModel viewModel, INavigationViewPageProvider pageProvider)
    {
        ViewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        ApplicationThemeManager.Apply(this);

        RootNavigation.SetPageProviderService(pageProvider);
        RootNavigation.Navigate(typeof(DashboardPage));
    }
}
