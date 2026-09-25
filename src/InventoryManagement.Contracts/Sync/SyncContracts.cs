namespace InventoryManagement.Contracts.Sync;

/// <summary>
/// The envelope wrapping every outbound synchronization operation sent
/// from a client (WPF today, potentially Angular eventually for whatever
/// subset applies to it) to the API. OperationId is the idempotency key
/// (Decision 9): if the same OperationId is submitted twice, the server
/// must recognize it as already processed rather than create duplicate
/// business data. On the client side, OperationId is simply the
/// originating OutboxOperation's own Id.
/// </summary>
public sealed record SyncOperationRequest(
    Guid OperationId,
    string OperationType,
    string EntityType,
    Guid EntityId,
    DateTimeOffset CreatedAtUtc,
    string PayloadJson);

public enum SyncOperationOutcome
{
    /// <summary>First time this OperationId was seen - processed now.</summary>
    Processed = 0,

    /// <summary>This OperationId was already processed previously - ResultJson below is the ORIGINAL result, not reprocessed.</summary>
    AlreadyProcessed = 1,

    /// <summary>Server-side validation rejected the operation (e.g. referenced entity no longer exists, insufficient stock at current server state).</summary>
    ValidationFailed = 2,

    /// <summary>Reserved for future conflict-resolution cases (see the migration report's Conflict Handling section) - not produced by anything in this foundation step.</summary>
    Conflict = 3,
}

public sealed record SyncOperationResponse(
    Guid OperationId,
    SyncOperationOutcome Outcome,
    string? ErrorMessage,
    string? ResultJson);
