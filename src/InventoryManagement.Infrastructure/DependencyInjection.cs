using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Infrastructure.Common;
using Microsoft.Extensions.DependencyInjection;

namespace InventoryManagement.Infrastructure;

/// <summary>
/// Registers everything the Infrastructure layer owns: EF Core/SQLite (from
/// Stage 2 onward), the date/time provider, and other concrete implementations
/// of Application-layer abstractions.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        // EF Core DbContext, repositories, and the backup engine are added
        // in their respective stages.

        return services;
    }
}
