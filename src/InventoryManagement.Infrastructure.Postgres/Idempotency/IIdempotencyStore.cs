namespace InventoryManagement.Infrastructure.Postgres.Idempotency;

/// <summary>
/// Decision 9: every synchronized operation must be idempotent. A
/// duplicate submission (the same OperationId seen twice - a genuinely
/// expected case, e.g. the client retried after a dropped response) must
/// return the ORIGINAL result rather than reprocess or silently no-op.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>Null if this OperationId has never been processed; otherwise the ORIGINAL result recorded for it.</summary>
    Task<ProcessedOperation?> FindAsync(Guid operationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that operationId has now been processed, with resultJson as
    /// its result. Must only be called once per OperationId - callers
    /// should FindAsync first and skip reprocessing entirely if a record
    /// already exists (see PurchaseSyncService, once built, for the
    /// pattern).
    /// </summary>
    Task RecordAsync(
        Guid operationId,
        string operationType,
        string entityType,
        Guid entityId,
        string resultJson,
        DateTimeOffset processedAtUtc,
        CancellationToken cancellationToken = default);
}
