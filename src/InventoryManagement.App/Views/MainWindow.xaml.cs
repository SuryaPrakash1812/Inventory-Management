using System.Windows;
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

        // NavigationView's internal content presenter isn't ready until its
        // control template has been applied, which hasn't happened yet at
        // constructor time - navigating here throws a NullReferenceException
        // deep inside UpdateContent. Deferring to Loaded guarantees the
        // template is applied first.
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        RootNavigation.Navigate(typeof(DashboardPage));
    }
}
