using Microsoft.Extensions.DependencyInjection;

namespace InventoryManagement.Application;

/// <summary>
/// Registers everything the Application layer owns. Each layer exposes exactly
/// one extension method like this, so Program.cs / App.xaml.cs stays a short,
/// readable list of "AddX()" calls instead of a wall of individual registrations.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Feature use-cases (e.g. IProductService, ISalesService) will be registered
        // here as each feature is implemented in its own stage.
        return services;
    }
}
