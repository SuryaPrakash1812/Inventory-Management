using InventoryManagement.Infrastructure.Postgres.Idempotency;
using InventoryManagement.Infrastructure.Postgres.Purchases;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InventoryManagement.Infrastructure.Postgres;

public static class DependencyInjection
{
    /// <summary>
    /// Registers PostgreSQL persistence for the API. connectionString comes
    /// from the API's own configuration (appsettings.json /
    /// environment variables) - this project has no opinion on where it
    /// comes from, only on how to use it once supplied.
    /// </summary>
    public static IServiceCollection AddInfrastructurePostgres(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<InventoryPostgresDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<PurchaseSyncService>();

        return services;
    }
}
