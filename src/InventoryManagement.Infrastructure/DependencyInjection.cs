using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Application.Settings;
using InventoryManagement.Infrastructure.Common;
using InventoryManagement.Infrastructure.Data;
using InventoryManagement.Infrastructure.Data.Interceptors;
using InventoryManagement.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InventoryManagement.Infrastructure;

/// <summary>
/// Registers everything the Infrastructure layer owns: EF Core/SQLite, the
/// date/time provider, settings persistence, and other concrete
/// implementations of Application-layer abstractions.
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

        return services;
    }
}
