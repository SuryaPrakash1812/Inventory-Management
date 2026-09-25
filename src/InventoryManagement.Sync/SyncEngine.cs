using System.Text.Json;
using InventoryManagement.Contracts.Purchases;
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
                    // Decision: "Local Purchase is updated with
                    // authoritative server information where necessary" -
                    // the user should never keep seeing their offline
                    // interim number after the real one exists. A missing
                    // or unparseable ResultJson is treated as a sync
                    // success without a number update rather than a
                    // failure - the purchase itself was genuinely created
                    // server-side either way, and a display-only field
                    // being briefly stale is a much smaller problem than
                    // marking a successful sync as failed and retrying it
                    // forever.
                    if (response.ResultJson is { Length: > 0 } resultJson)
                    {
                        try
                        {
                            var result = JsonSerializer.Deserialize<PurchaseSyncResultContract>(resultJson);
                            if (result is not null)
                            {
                                await _outboxProcessor.ApplyServerPurchaseNumberAsync(
                                    result.PurchaseId, result.ServerPurchaseNumber, cancellationToken);
                            }
                        }
                        catch (JsonException)
                        {
                            // Malformed result payload - the sync itself
                            // still succeeded (see remarks above), so this
                            // is deliberately swallowed rather than failing
                            // the whole operation.
                        }
                    }

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
                // treated as TRANSIENT: returned to Pending with RetryCount
                // incremented, so a later sync run (after backoff) tries
                // again automatically, per "transient failure: retry using
                // controlled backoff." Distinct from a ValidationFailed
                // response above, which is a permanent business rejection
                // and must not be retried the same way.
                await _outboxProcessor.MarkTransientFailureAsync(operation.Id, ex.Message, cancellationToken);
                failed++;
            }
        }

        return new SyncRunSummary(pending.Count, succeeded, failed);
    }
}
