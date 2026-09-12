using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using InventoryManagement.App.Messages;
using InventoryManagement.App.Services;
using InventoryManagement.App.Views.Pages;
using InventoryManagement.Application.Auth;
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
///
/// Navigation items are filtered by <see cref="IAuthorizationService"/> at
/// construction time (which happens only after a successful sign-in - see
/// App.xaml.cs), so a user simply never sees a menu entry for something they
/// don't have permission to open. This is the "do not hard-code authorization
/// logic throughout the UI" requirement: every future page just declares the
/// one permission name it needs here, instead of every page re-implementing
/// its own visibility checks.
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

    public string UserDisplayName { get; }

    public ObservableCollection<object> MenuItems { get; } = new();

    public ObservableCollection<object> FooterMenuItems { get; } = new();

    public ShellViewModel(
        ISettingsService settingsService,
        IAuthorizationService authorizationService,
        ICurrentUserSession session)
    {
        _settingsService = settingsService;
        _isDarkTheme = settingsService.Current.Theme == ThemeMode.Dark;

        UserDisplayName = session.CurrentUser?.FullName ?? "Unknown User";

        void AddIfAuthorized(
            ObservableCollection<object> collection, string title, SymbolRegular icon, Type pageType, string permission)
        {
            if (authorizationService.HasPermission(permission))
            {
                collection.Add(new NavigationViewItem(title, icon, pageType));
            }
        }

        AddIfAuthorized(MenuItems, "Dashboard", SymbolRegular.Home24, typeof(DashboardPage), "Dashboard.View");
        AddIfAuthorized(MenuItems, "Products", SymbolRegular.Box24, typeof(ProductsPage), "Products.View");
        AddIfAuthorized(MenuItems, "Categories", SymbolRegular.Tag24, typeof(CategoriesPage), "Products.View");
        AddIfAuthorized(MenuItems, "Suppliers", SymbolRegular.Building24, typeof(SuppliersPage), "Purchases.View");
        AddIfAuthorized(MenuItems, "Customers", SymbolRegular.PeopleTeam24, typeof(CustomersPage), "Sales.View");
        AddIfAuthorized(MenuItems, "Purchases", SymbolRegular.ShoppingBag24, typeof(PurchasesPage), "Purchases.View");
        AddIfAuthorized(MenuItems, "Sales", SymbolRegular.ReceiptMoney24, typeof(SalesPage), "Sales.View");
        AddIfAuthorized(MenuItems, "Inventory", SymbolRegular.BoxMultiple24, typeof(InventoryPage), "Inventory.View");
        AddIfAuthorized(MenuItems, "Stock Adjustments", SymbolRegular.ArrowSync24, typeof(StockAdjustmentsPage), "Inventory.Adjust");
        AddIfAuthorized(MenuItems, "Reports", SymbolRegular.DataHistogram24, typeof(ReportsPage), "Reports.View");
        AddIfAuthorized(MenuItems, "Users", SymbolRegular.Person24, typeof(UsersPage), "Users.View");
        AddIfAuthorized(MenuItems, "Backup", SymbolRegular.CloudArrowUp24, typeof(BackupPage), "Backup.Create");

        AddIfAuthorized(FooterMenuItems, "Settings", SymbolRegular.Settings24, typeof(SettingsPage), "Settings.Manage");
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

    [RelayCommand]
    private void Logout()
    {
        WeakReferenceMessenger.Default.Send(new LogoutRequestedMessage());
    }
}
