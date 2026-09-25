using System.Net.Http.Json;
using InventoryManagement.Contracts.Sync;

namespace InventoryManagement.Sync.Api;

public sealed class HttpApiClient : IApiClient
{
    private readonly HttpClient _httpClient;

    public HttpApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<SyncOperationResponse> SendPurchaseCreateAsync(
        SyncOperationRequest request, CancellationToken cancellationToken = default)
    {
        var httpResponse = await _httpClient.PostAsJsonAsync("/api/sync/purchases", request, cancellationToken);
        httpResponse.EnsureSuccessStatusCode();

        var response = await httpResponse.Content.ReadFromJsonAsync<SyncOperationResponse>(cancellationToken: cancellationToken);
        return response ?? throw new InvalidOperationException("API returned an empty sync response.");
    }
}
