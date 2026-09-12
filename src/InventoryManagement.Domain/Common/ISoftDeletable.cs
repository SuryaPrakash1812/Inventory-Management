namespace InventoryManagement.Domain.Common;

/// <summary>
/// Implemented by entities that must never be physically deleted (products,
/// customers, sales, etc.) because history and reports depend on them. The
/// data layer applies a global query filter based on <see cref="IsDeleted"/>.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; }
    DateTimeOffset? DeletedAtUtc { get; }
    Guid? DeletedByUserId { get; }
}
