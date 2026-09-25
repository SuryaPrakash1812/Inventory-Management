using InventoryManagement.Sync.Api;
using InventoryManagement.Sync.Connectivity;
using InventoryManagement.Sync.Outbox;
using Microsoft.Extensions.DependencyInjection;

namespace InventoryManagement.Sync;

public static class DependencyInjection
{
    private const string HttpClientName = "InventoryApi";

    /// <summary>
    /// Registers the Sync Engine and its dependencies. apiBaseAddress is
    /// the API's base URL (e.g. from a WPF app setting) - deliberately a
    /// parameter, not hardcoded, since this must be configurable per
    /// deployment (and per developer machine during development).
    ///
    /// IConnectivityService and IApiClient are registered as true
    /// singletons via IHttpClientFactory rather than through AddHttpClient's
    /// typed-client shortcut (AddHttpClient&lt;TClient,TImpl&gt;), which
    /// registers TImpl as TRANSIENT by default - that would silently break
    /// IConnectivityService, whose entire design depends on being one
    /// stable instance holding cached state and a single NetworkChange
    /// subscription, not a fresh instance per resolution.
    ///
    /// Registering this does NOT make anything run automatically - see
    /// ISyncEngine's remarks. It only makes ISyncEngine (and its pieces)
    /// available to be called explicitly, and makes IConnectivityService
    /// available for other services' routing decisions.
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

        return services;
    }
}
