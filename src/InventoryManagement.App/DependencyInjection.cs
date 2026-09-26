using InventoryManagement.App.ViewModels;
using InventoryManagement.App.ViewModels.Auth;
using InventoryManagement.App.ViewModels.Categories;
using InventoryManagement.App.ViewModels.Customers;
using InventoryManagement.App.ViewModels.Dashboard;
using InventoryManagement.App.ViewModels.Inventory;
using InventoryManagement.App.ViewModels.Products;
using InventoryManagement.App.ViewModels.Purchases;
using InventoryManagement.App.ViewModels.Sales;
using InventoryManagement.App.ViewModels.StockAdjustments;
using InventoryManagement.App.ViewModels.Suppliers;
using InventoryManagement.App.ViewModels.Users;
using InventoryManagement.App.Views;
using InventoryManagement.App.Views.Auth;
using InventoryManagement.App.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.DependencyInjection;

namespace InventoryManagement.App;

/// <summary>
/// Registers everything the presentation (WPF/App) layer owns: the login
/// window, the shell window, the WPF-UI navigation page provider, and every
/// page + its ViewModel. Mirrors the AddApplication()/AddInfrastructure()
/// pattern used by the other layers.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        // Lets WPF-UI's NavigationView resolve pages through this DI container
        // instead of calling "new PageType()" itself.
        services.AddNavigationViewPageProvider();

        services.AddTransient<LoginWindow>();
        services.AddTransient<LoginViewModel>();

        // MainWindow and ShellViewModel are deliberately Transient, not
        // Singleton: ShellViewModel builds its permission-filtered nav menu
        // once, at construction, from whoever is currently signed in. If it
        // were a Singleton, the very first user's menu (and permissions)
        // would be cached forever - a second user signing in after a logout
        // would still see the first user's menu. A fresh instance per login
        // is required for the logout/re-login flow to behave correctly.
        services.AddTransient<MainWindow>();
        services.AddTransient<ShellViewModel>();

        // Pages are transient: NavigationView creates a fresh instance on every
        // navigation by default (CacheHistory is 0), so there is no state to
        // preserve between visits for these simple pages.
        services.AddTransient<DashboardPage>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ProductsPage>();
        services.AddTransient<ProductsViewModel>();
        services.AddTransient<CategoriesPage>();
        services.AddTransient<CategoriesViewModel>();
        services.AddTransient<SuppliersPage>();
        services.AddTransient<SuppliersViewModel>();
        services.AddTransient<CustomersPage>();
        services.AddTransient<CustomersViewModel>();
        services.AddTransient<PurchasesPage>();
        services.AddTransient<PurchasesViewModel>();
        services.AddTransient<SalesPage>();
        services.AddTransient<SalesViewModel>();
        services.AddTransient<InventoryPage>();
        services.AddTransient<InventoryViewModel>();
        services.AddTransient<StockAdjustmentsPage>();
        services.AddTransient<StockAdjustmentsViewModel>();
        services.AddTransient<ReportsPage>();
        services.AddTransient<BackupPage>();

        services.AddTransient<UsersPage>();
        services.AddTransient<UsersViewModel>();

        services.AddTransient<SettingsPage>();
        services.AddTransient<SettingsViewModel>();

        return services;
    }
}
