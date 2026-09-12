using InventoryManagement.App.ViewModels;
using InventoryManagement.App.Views;
using InventoryManagement.App.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.DependencyInjection;

namespace InventoryManagement.App;

/// <summary>
/// Registers everything the presentation (WPF/App) layer owns: the shell
/// window, the WPF-UI navigation page provider, and every page + its
/// ViewModel. Mirrors the AddApplication()/AddInfrastructure() pattern used
/// by the other layers.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        // Lets WPF-UI's NavigationView resolve pages through this DI container
        // instead of calling "new PageType()" itself.
        services.AddNavigationViewPageProvider();

        services.AddSingleton<MainWindow>();
        services.AddSingleton<ShellViewModel>();

        // Pages are transient: NavigationView creates a fresh instance on every
        // navigation by default (CacheHistory is 0), so there is no state to
        // preserve between visits for these simple pages.
        services.AddTransient<DashboardPage>();
        services.AddTransient<ProductsPage>();
        services.AddTransient<CategoriesPage>();
        services.AddTransient<SuppliersPage>();
        services.AddTransient<CustomersPage>();
        services.AddTransient<PurchasesPage>();
        services.AddTransient<SalesPage>();
        services.AddTransient<InventoryPage>();
        services.AddTransient<StockAdjustmentsPage>();
        services.AddTransient<ReportsPage>();
        services.AddTransient<UsersPage>();
        services.AddTransient<BackupPage>();

        services.AddTransient<SettingsPage>();
        services.AddTransient<SettingsViewModel>();

        return services;
    }
}
