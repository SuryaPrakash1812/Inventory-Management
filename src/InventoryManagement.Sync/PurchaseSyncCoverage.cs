namespace InventoryManagement.Sync;

/// <summary>
/// Explicit record of Purchase operation sync coverage, per the
/// architecture decision that this must never be left implicit. Every
/// operation below works fully offline against local SQLite regardless of
/// its sync status - "not yet synced" does NOT mean "requires connectivity
/// to perform" (that would contradict this application's offline-first
/// design). It means: the operation's local effect is not yet mirrored to
/// PostgreSQL when connectivity returns.
///
/// | Operation      | OperationType    | Contract               | Endpoint                 | Server handler      | Idempotency          | Status               |
/// |----------------|------------------|-------------------------|--------------------------|----------------------|-----------------------|----------------------|
/// | Create draft   | Purchase.Create  | CreatePurchaseContract  | POST /api/sync/purchases | PurchaseSyncService  | OperationId, via      | SYNCED. Enqueued by  |
/// |                |                  |                         |                          | .CreatePurchaseAsync | ProcessedOperations   | PurchaseService.     |
/// |                |                  |                         |                          |                      |                       | EnqueueCreatePurchase|
/// |                |                  |                         |                          |                      |                       | OutboxOperation.     |
/// | Edit draft     | -                | -                       | -                        | -                    | -                     | LOCAL ONLY.          |
/// | Confirm        | -                | -                       | -                        | -                    | -                     | LOCAL ONLY.          |
/// | Cancel         | -                | -                       | -                        | -                    | -                     | LOCAL ONLY.          |
/// | Payment status | -                | -                       | -                        | -                    | -                     | LOCAL ONLY.          |
/// | Delete draft   | -                | -                       | -                        | -                    | -                     | LOCAL ONLY.          |
///
/// Enforcement of this scope lives in two places, deliberately, so it
/// cannot silently drift out of sync with this table:
/// 1. PurchaseService only ever calls EnqueueCreatePurchaseOutboxOperation
///    for a brand-new purchase (auditAction == AuditAction.Created) - see
///    its own remarks for why editing an existing draft does not also
///    queue one.
/// 2. SyncEngine.SyncPendingOperationsAsync explicitly checks
///    OperationType == "Purchase.Create" and marks anything else a
///    PERMANENT failure ("not yet supported by the Sync Engine") rather
///    than attempting to process it - this is what guarantees the Outbox
///    can never contain an operation the Sync Engine silently drops or
///    retries forever without explanation, per the architecture decision.
///
/// Extending sync coverage to Edit/Confirm/Cancel/PaymentStatus/Delete is
/// explicitly future work - each needs its own Contract, endpoint, and
/// PurchaseSyncService handler performing independent server-side
/// re-validation (the same pattern CreatePurchaseAsync already
/// establishes), which is a genuinely separate task per operation, not
/// mechanical repetition. Confirm in particular needs care: it creates
/// StockMovement rows, and the server must independently re-validate
/// stock/business rules at sync time rather than trust whatever the
/// client believed was true when it confirmed offline (see the migration
/// report's Conflict Handling section for the scenarios this raises).
/// </summary>
public static class PurchaseSyncCoverage
{
}
