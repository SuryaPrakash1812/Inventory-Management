using InventoryManagement.Application.Purchases;
using InventoryManagement.Infrastructure.Data;
using InventoryManagement.Infrastructure.Purchases;
using InventoryManagement.Sync.Api;
using InventoryManagement.Sync.Connectivity;
using InventoryManagement.Sync.Outbox;
using InventoryManagement.Sync.Purchases;
using Microsoft.Extensions.DependencyInjection;

namespace InventoryManagement.Sync;

public static class DependencyInjection
{
    private const string HttpClientName = "InventoryApi";

    /// <summary>
    /// Registers the Sync Engine and its dependencies, AND overrides
    /// IPurchaseService to resolve to PurchaseServiceRouter instead of the
    /// plain local PurchaseService - this is what actually turns online
    /// routing on. Microsoft.Extensions.DependencyInjection resolves the
    /// LAST registration for a given service type, so this only takes
    /// effect if AddSync is called AFTER AddInfrastructure (which
    /// registers IPurchaseService -> PurchaseService) in the app's
    /// composition root. If AddSync is never called at all, the app keeps
    /// its exact original pure-local behavior with zero change - adding
    /// online capability is an explicit opt-in at the composition root,
    /// not an automatic side effect of this project merely existing.
    ///
    /// apiBaseAddress is the API's base URL (e.g. from a WPF app setting) -
    /// deliberately a parameter, not hardcoded, since this must be
    /// configurable per deployment (and per developer machine during
    /// development).
    ///
    /// IConnectivityService and IApiClient are registered as true
    /// singletons via IHttpClientFactory rather than through AddHttpClient's
    /// typed-client shortcut (AddHttpClient&lt;TClient,TImpl&gt;), which
    /// registers TImpl as TRANSIENT by default - that would silently break
    /// IConnectivityService, whose entire design depends on being one
    /// stable instance holding cached state and a single NetworkChange
    /// subscription, not a fresh instance per resolution.
    /// </summary>
    public static IServiceCollection AddSync(this IServiceCollection services, Uri apiBaseAddress)
    {
        services.AddHttpClient(HttpClientName, client =>
        {
            client.BaseAddress = apiBaseAddress;
        });

        services.AddSingleton<IApiClient>(sp =>
            new HttpApiClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName)));

        services.AddSingleton<IConnectivityService>(sp =>
            new ConnectivityService(sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName)));

        services.AddSingleton<IOutboxProcessor, OutboxProcessor>();
        services.AddSingleton<ISyncEngine, SyncEngine>();

        // The concrete PurchaseService is what AddInfrastructure already
        // registers IPurchaseService to resolve to - self-registering it
        // under its own concrete type here lets the router depend on it
        // directly without a circular IPurchaseService -> router ->
        // IPurchaseService resolution loop.
        services.AddScoped<PurchaseService>();

        services.AddScoped<IPurchaseService>(sp => new PurchaseServiceRouter(
            sp.GetRequiredService<PurchaseService>(),
            sp.GetRequiredService<IConnectivityService>(),
            sp.GetRequiredService<IApiClient>(),
            sp.GetRequiredService<IAppDbContext>()));

        return services;
    }
}
