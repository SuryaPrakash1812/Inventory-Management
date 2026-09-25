using InventoryManagement.Domain.Common;
using InventoryManagement.Domain.Enums;

namespace InventoryManagement.Domain.Entities;

/// <summary>
/// One pending (or completed) business operation awaiting synchronization
/// to the server, per Decision 8 of the online/offline architecture
/// migration. This entity's own Id (inherited from BaseEntity - a
/// client-generated GUID, assigned at construction, before this row is
/// ever saved) IS the OperationId referenced everywhere else - the
/// idempotency key sent to the API, and the key the server's
/// ProcessedOperations table is checked/written against (Decision 9). No
/// separate field is needed for it.
///
/// A row here must only ever exist for a business operation that was
/// ALREADY successfully committed locally in the same SQLite transaction -
/// see PurchaseService (once wired to write these) for the "business data
/// + OutboxOperation, one SaveChangesAsync call, or neither" pattern this
/// entity is designed around. This class intentionally carries no
/// behavior - it is a plain outbound queue record.
/// </summary>
public class OutboxOperation : BaseEntity
{
    /// <summary>E.g. "Purchase.Create", "Purchase.Confirm", "Purchase.Cancel" - identifies which business operation Payload represents, for the Sync Engine and the API to route/interpret it correctly.</summary>
    public string OperationType { get; set; } = string.Empty;

    /// <summary>E.g. "Purchase" - the Domain entity type this operation concerns.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>The affected entity's own Id (e.g. the Purchase's Id) - distinct from this row's own Id/OperationId.</summary>
    public Guid EntityId { get; set; }

    /// <summary>JSON-serialized business operation payload - see InventoryManagement.Contracts for the shapes actually sent over the wire.</summary>
    public string PayloadJson { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public int RetryCount { get; set; }

    public DateTimeOffset? LastAttemptAtUtc { get; set; }

    public OutboxOperationStatus Status { get; set; } = OutboxOperationStatus.Pending;

    public string? ErrorMessage { get; set; }
}
