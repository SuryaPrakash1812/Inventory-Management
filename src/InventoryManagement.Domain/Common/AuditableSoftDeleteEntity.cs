namespace InventoryManagement.Domain.Common;

/// <summary>
/// Base class for the main business entities (Product, Customer, Purchase,
/// etc.) that must never be physically deleted, since reports and history
/// depend on them, but still need a full created/modified/deleted audit
/// trail. Use <see cref="MarkDeleted"/>/<see cref="Restore"/> rather than
/// setting the properties directly, so the intent is always explicit at the
/// call site.
/// </summary>
public abstract class AuditableSoftDeleteEntity : AuditableEntity, ISoftDeletable
{
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAtUtc { get; private set; }
    public Guid? DeletedByUserId { get; private set; }

    public void MarkDeleted(DateTimeOffset atUtc, Guid? byUserId)
    {
        IsDeleted = true;
        DeletedAtUtc = atUtc;
        DeletedByUserId = byUserId;
    }

    public void Restore()
    {
        IsDeleted = false;
        DeletedAtUtc = null;
        DeletedByUserId = null;
    }
}
