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
        var skipped = 0;

        foreach (var operation in pending)
        {
            // Only Purchase.Create has a server-side handler
            // (PurchaseSyncService) right now. Per the offline-completeness
            // stage's strategy, every Purchase-mutating operation queues an
            // Outbox entry regardless - the Outbox's job is to durably
            // record what happened locally for FUTURE synchronization, not
            // to only record what can already be sent. An unsupported type
            // is deliberately left untouched (still Pending, not marked
            // Failed) - it isn't a failure, it's correctly waiting for a
            // server handler that doesn't exist yet, and will be picked up
            // automatically the moment a future stage adds one, with no
            // migration or backfill needed for rows already sitting here.
            if (operation.OperationType != "Purchase.Create")
            {
                skipped++;
                continue;
            }

            try
            {
                var request = new SyncOperationRequest(
                    operation.Id, operation.OperationType, operation.EntityType, operation.EntityId,
                    operation.CreatedAtUtc, operation.PayloadJson);

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

        return new SyncRunSummary(pending.Count, succeeded, failed, skipped);
    }
}
