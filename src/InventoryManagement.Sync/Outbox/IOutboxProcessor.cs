using InventoryManagement.Domain.Entities;

namespace InventoryManagement.Sync.Outbox;

public interface IOutboxProcessor
{
    Task<IReadOnlyList<OutboxOperation>> GetPendingOperationsAsync(CancellationToken cancellationToken = default);

    Task MarkSyncedAsync(Guid operationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// A PERMANENT/business failure (e.g. server-side validation rejected
    /// it) - terminal. The operation stays out of GetPendingOperationsAsync
    /// forever after this, retaining its payload and error message for an
    /// admin to review, rather than being retried indefinitely against a
    /// request that can never succeed as-is.
    /// </summary>
    Task MarkFailedAsync(Guid operationId, string errorMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// A TRANSIENT failure (network timeout, connection refused, 5xx from
    /// the API, etc.) - NOT terminal. The operation returns to Pending so
    /// a later sync run picks it up again, with RetryCount incremented and
    /// LastAttemptAtUtc recorded so GetPendingOperationsAsync can apply
    /// backoff (skip operations retried too recently) rather than
    /// hammering a struggling server every sync run.
    /// </summary>
    Task MarkTransientFailureAsync(Guid operationId, string errorMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the server's authoritative PurchaseNumber back to the local
    /// SQLite Purchase record after a successful "Purchase.Create" sync -
    /// without this, the user would keep seeing their offline-generated
    /// interim number (e.g. PO-A1B2C3-00001) forever, even after the
    /// purchase has a real server-assigned one (e.g. PO-2026-000123).
    /// </summary>
    Task ApplyServerPurchaseNumberAsync(Guid purchaseId, string serverPurchaseNumber, CancellationToken cancellationToken = default);
}
