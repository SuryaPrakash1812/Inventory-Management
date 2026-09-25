namespace InventoryManagement.Infrastructure.Postgres.Idempotency;

/// <summary>
/// One row per OperationId the server has ever processed, satisfying
/// Decision 9: if the same OperationId is submitted twice, the server
/// must return the ORIGINAL result rather than process it again. This is
/// deliberately separate from any Domain entity - it is not business
/// data, it is bookkeeping for the sync mechanism itself, and only ever
/// exists in PostgreSQL (the local SQLite side has no equivalent need -
/// it is the ORIGIN of operations, not the de-duplication point).
/// </summary>
public class ProcessedOperation
{
    /// <summary>The OperationId from the client's SyncOperationRequest - the primary key. Deliberately not a generated Id: this IS the natural key.</summary>
    public Guid OperationId { get; set; }

    public string OperationType { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    public DateTimeOffset ProcessedAtUtc { get; set; }

    /// <summary>The original SyncOperationResponse.ResultJson, returned again verbatim if this OperationId is ever resubmitted.</summary>
    public string ResultJson { get; set; } = string.Empty;
}
