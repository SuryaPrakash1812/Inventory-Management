namespace InventoryManagement.Domain.Common;

/// <summary>
/// Implemented by entities that need a created/modified audit trail. EF Core
/// interceptors (added in the data-layer stage) populate these fields
/// automatically on SaveChanges, so individual features never set them by hand.
/// </summary>
public interface IAuditableEntity
{
    DateTimeOffset CreatedAtUtc { get; }
    Guid? CreatedByUserId { get; }
    DateTimeOffset? ModifiedAtUtc { get; }
    Guid? ModifiedByUserId { get; }
}
