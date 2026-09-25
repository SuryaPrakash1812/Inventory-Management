using InventoryManagement.Contracts.Sync;
using InventoryManagement.Sync.Api;
using InventoryManagement.Sync.Connectivity;
using InventoryManagement.Sync.Outbox;

namespace InventoryManagement.Sync;

public sealed class SyncEngine : ISyncEngine
{
    private readonly IOutboxProcessor _outboxProcessor;
    private readonly IApiClient _apiClient;
    private readonly IConnectivityService _connectivityService;

    public SyncEngine(IOutboxProcessor outboxProcessor, IApiClient apiClient, IConnectivityService connectivityService)
    {
        _outboxProcessor = outboxProcessor;
        _apiClient = apiClient;
        _connectivityService = connectivityService;
    }

    public async Task<SyncRunSummary> SyncPendingOperationsAsync(CancellationToken cancellationToken = default)
    {
        if (!_connectivityService.IsOnline)
        {
            return new SyncRunSummary(0, 0, 0);
        }

        var pending = await _outboxProcessor.GetPendingOperationsAsync(cancellationToken);

        var succeeded = 0;
        var failed = 0;

        foreach (var operation in pending)
        {
            try
            {
                var request = new SyncOperationRequest(
                    operation.Id, operation.OperationType, operation.EntityType, operation.EntityId,
                    operation.CreatedAtUtc, operation.PayloadJson);

                // Only Purchase.Create is wired up in this foundation step
                // (matches PurchaseSyncEndpoints on the API side). Anything
                // else currently in the Outbox is marked failed rather than
                // silently dropped or retried forever against an endpoint
                // that does not exist yet.
                if (operation.OperationType != "Purchase.Create")
                {
                    await _outboxProcessor.MarkFailedAsync(
                        operation.Id,
                        $"Operation type '{operation.OperationType}' is not yet supported by the Sync Engine.",
                        cancellationToken);
                    failed++;
                    continue;
                }

                var response = await _apiClient.SendPurchaseCreateAsync(request, cancellationToken);

                if (response.Outcome is SyncOperationOutcome.Processed or SyncOperationOutcome.AlreadyProcessed)
                {
                    await _outboxProcessor.MarkSyncedAsync(operation.Id, cancellationToken);
                    succeeded++;
                }
                else
                {
                    await _outboxProcessor.MarkFailedAsync(
                        operation.Id, response.ErrorMessage ?? "Unknown sync failure.", cancellationToken);
                    failed++;
                }
            }
            catch (Exception ex)
            {
                // Network failure, timeout, or anything else unexpected -
                // marked Failed (with RetryCount incremented) rather than
                // left Pending forever unexplained. No retry/backoff
                // scheduling here yet - that is complete-Sync-Engine work.
                await _outboxProcessor.MarkFailedAsync(operation.Id, ex.Message, cancellationToken);
                failed++;
            }
        }

        return new SyncRunSummary(pending.Count, succeeded, failed);
    }
}
