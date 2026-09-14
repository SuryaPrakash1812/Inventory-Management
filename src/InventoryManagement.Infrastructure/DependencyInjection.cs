using InventoryManagement.Application.Auth;
using InventoryManagement.Application.Categories;
using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Application.Customers;
using InventoryManagement.Application.Inventory;
using InventoryManagement.Application.Products;
using InventoryManagement.Application.Purchases;
using InventoryManagement.Application.Settings;
using InventoryManagement.Application.Suppliers;
using InventoryManagement.Application.Users;
using InventoryManagement.Infrastructure.Auth;
using InventoryManagement.Infrastructure.Categories;
using InventoryManagement.Infrastructure.Common;
using InventoryManagement.Infrastructure.Customers;
using InventoryManagement.Infrastructure.Data;
using InventoryManagement.Infrastructure.Data.Interceptors;
using InventoryManagement.Infrastructure.Inventory;
using InventoryManagement.Infrastructure.Products;
using InventoryManagement.Infrastructure.Purchases;
using InventoryManagement.Infrastructure.Settings;
using InventoryManagement.Infrastructure.Suppliers;
using InventoryManagement.Infrastructure.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InventoryManagement.Infrastructure;

/// <summary>
/// Registers everything the Infrastructure layer owns: EF Core/SQLite, the
/// date/time provider, settings persistence, authentication/authorization,
/// and other concrete implementations of Application-layer abstractions.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<ISettingsService, JsonSettingsService>();

        services.AddSingleton<AuditableEntitySaveChangesInterceptor>();
        services.AddSingleton<SqliteConnectionInterceptor>();

        services.AddDbContext<InventoryDbContext>((sp, options) =>
        {
            options.UseSqlite($"Data Source={AppPaths.DatabaseFilePath}");
            options.AddInterceptors(
                sp.GetRequiredService<AuditableEntitySaveChangesInterceptor>(),
                sp.GetRequiredService<SqliteConnectionInterceptor>());
        });

        // IAppDbContext resolves to the same scoped InventoryDbContext instance -
        // Application-layer code depends on the interface, never the concrete type.
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<InventoryDbContext>());

        services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();

        // Auth: password hashing and the authorization check are stateless
        // (or depend only on the singleton session), so they can be
        // singletons. The current-user session itself must be a singleton -
        // it holds in-memory state for the whole app run. Everything that
        // touches the database is Scoped to match InventoryDbContext; the
        // app resolves these from one long-lived scope created at startup
        // (see App.xaml.cs), since a desktop app has no natural per-request
        // scope boundary the way a web app does.
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ICurrentUserSession, CurrentUserSession>();
        services.AddSingleton<IAuthorizationService, AuthorizationService>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IRoleManagementService, RoleManagementService>();

        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IPurchaseService, PurchaseService>();

        return services;
    }
}
