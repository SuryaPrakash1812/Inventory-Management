using InventoryManagement.Contracts.Sync;

namespace InventoryManagement.Sync.Api;

/// <summary>
/// Abstraction over "send this operation to the API" - kept separate from
/// ISyncEngine so the HTTP mechanics can be swapped or mocked
/// independently of the drain-the-outbox orchestration logic. Scoped to
/// Purchase.Create only for this foundation step - one method per
/// operation type is added as each type is wired into real sync support,
/// matching how the API side (PurchaseSyncEndpoints) is scoped the same
/// way right now.
/// </summary>
public interface IApiClient
{
    Task<SyncOperationResponse> SendPurchaseCreateAsync(SyncOperationRequest request, CancellationToken cancellationToken = default);
}
