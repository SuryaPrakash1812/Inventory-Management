using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InventoryManagement.App.Services;
using InventoryManagement.App.Views.Pages;
using InventoryManagement.Application.Settings;
using Wpf.Ui.Controls;

namespace InventoryManagement.App.ViewModels;

/// <summary>
/// ViewModel for the application shell (<see cref="Views.MainWindow"/>). Owns
/// the navigation menu definition, the currently-displayed page's title, the
/// theme toggle, and the small amount of shell-wide state (status message,
/// signed-in user) shown in the header/status bar.
///
/// Each page is responsible for setting <see cref="CurrentPageTitle"/> itself
/// when it is navigated to (see the page constructors), rather than the shell
/// depending on NavigationView's event API - this keeps the shell decoupled
/// from navigation-event wiring details.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;

    public string WindowTitle => "Inventory Management";

    [ObservableProperty]
    private string _currentPageTitle = "Dashboard";

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isDarkTheme;

    /// <summary>Placeholder until Stage 9 (Users/Auth) introduces real sessions.</summary>
    public string UserDisplayName => "Local User";

    public ObservableCollection<object> MenuItems { get; } = new();

    public ObservableCollection<object> FooterMenuItems { get; } = new();

    public ShellViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _isDarkTheme = settingsService.Current.Theme == ThemeMode.Dark;

        MenuItems.Add(new NavigationViewItem("Dashboard", SymbolRegular.Home24, typeof(DashboardPage)));
        MenuItems.Add(new NavigationViewItem("Products", SymbolRegular.Box24, typeof(ProductsPage)));
        MenuItems.Add(new NavigationViewItem("Categories", SymbolRegular.Tag24, typeof(CategoriesPage)));
        MenuItems.Add(new NavigationViewItem("Suppliers", SymbolRegular.Building24, typeof(SuppliersPage)));
        MenuItems.Add(new NavigationViewItem("Customers", SymbolRegular.PeopleTeam24, typeof(CustomersPage)));
        MenuItems.Add(new NavigationViewItem("Purchases", SymbolRegular.ShoppingBag24, typeof(PurchasesPage)));
        MenuItems.Add(new NavigationViewItem("Sales", SymbolRegular.ReceiptMoney24, typeof(SalesPage)));
        MenuItems.Add(new NavigationViewItem("Inventory", SymbolRegular.BoxMultiple24, typeof(InventoryPage)));
        MenuItems.Add(new NavigationViewItem("Stock Adjustments", SymbolRegular.ArrowSync24, typeof(StockAdjustmentsPage)));
        MenuItems.Add(new NavigationViewItem("Reports", SymbolRegular.DataHistogram24, typeof(ReportsPage)));
        MenuItems.Add(new NavigationViewItem("Users", SymbolRegular.Person24, typeof(UsersPage)));
        MenuItems.Add(new NavigationViewItem("Backup", SymbolRegular.CloudArrowUp24, typeof(BackupPage)));

        FooterMenuItems.Add(new NavigationViewItem("Settings", SymbolRegular.Settings24, typeof(SettingsPage)));
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;

        var mode = IsDarkTheme ? ThemeMode.Dark : ThemeMode.Light;
        _settingsService.Current.Theme = mode;
        _ = _settingsService.SaveAsync();

        ThemeApplier.Apply(mode);
    }
}
