namespace InventoryManagement.Sync;

/// <summary>
/// Explicit record of Purchase operation sync coverage, per the
/// architecture decision that this must never be left implicit.
///
/// Updated for the offline-completeness stage: EVERY Purchase-mutating
/// operation now queues an Outbox entry (Section 11 of that stage - there
/// must be no silent case where SQLite changes but has no representation
/// for future synchronization), not just Create as in the earlier
/// architecture-foundation stage. What changed is which operations queue
/// an Outbox row; what has NOT changed is which operations have a
/// server-side handler that can actually PROCESS one yet.
///
/// | Operation      | OperationType             | Contract                        | Outbox queued | Server handler exists |
/// |----------------|----------------------------|----------------------------------|----------------|-------------------------|
/// | Create draft   | Purchase.Create            | CreatePurchaseContract           | YES            | YES - PurchaseSyncService.CreatePurchaseAsync |
/// | Edit draft     | Purchase.Update            | UpdatePurchaseContract           | YES            | NO - accumulates Pending |
/// | Confirm        | Purchase.Confirm           | ConfirmPurchaseContract          | YES            | NO - accumulates Pending |
/// | Cancel         | Purchase.Cancel            | CancelPurchaseContract           | YES            | NO - accumulates Pending |
/// | Payment status | Purchase.SetPaymentStatus  | SetPurchasePaymentStatusContract | YES            | NO - accumulates Pending |
/// | Delete draft   | Purchase.Delete            | DeletePurchaseContract           | YES            | NO - accumulates Pending |
///
/// Every offline operation above works fully regardless of this table -
/// "no server handler yet" means the resulting Outbox row simply waits
/// (left Pending, not marked Failed - see SyncEngine's remarks) until a
/// later stage adds one. It does NOT mean the operation requires
/// connectivity to perform locally; that would contradict this
/// application's offline-first design.
///
/// Enforcement lives in two places, deliberately, so it cannot silently
/// drift out of sync with this table:
/// 1. PurchaseService.EnqueueOutboxOperation is called from every
///    mutating method (SaveDraftAsync for both Create and Update,
///    ConfirmPurchaseAsync, CancelPurchaseAsync, SetPaymentStatusAsync,
///    DeleteDraftAsync) - a new mutating method that forgets to call it is
///    the only way this table could go stale.
/// 2. SyncEngine.SyncPendingOperationsAsync explicitly checks
///    OperationType == "Purchase.Create" before attempting to process
///    anything - every other type is left untouched (still Pending) each
///    run, rather than the Sync Engine guessing at how to handle a type it
///    has no logic for.
///
/// Building the remaining server handlers (Update/Confirm/Cancel/
/// SetPaymentStatus/Delete) is explicitly future work. Confirm and Cancel
/// need particular care: they affect stock, and the server must
/// independently re-validate against its own current PostgreSQL state
/// rather than trust whatever the client believed was true when it
/// confirmed/cancelled offline (see the migration report's Conflict
/// Handling section).
/// </summary>
public static class PurchaseSyncCoverage
{
}
