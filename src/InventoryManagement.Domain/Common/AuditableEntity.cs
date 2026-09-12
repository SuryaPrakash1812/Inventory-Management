namespace InventoryManagement.Domain.Common;

/// <summary>
/// Base class for entities that need a created/modified audit trail but where
/// soft-delete doesn't make sense (lookup and reference data such as
/// <see cref="Entities.Permission"/> or <see cref="Entities.ApplicationSetting"/>).
/// A SaveChanges interceptor (Infrastructure layer) populates the audit
/// fields automatically - entities never set them directly.
/// </summary>
public abstract class AuditableEntity : BaseEntity, IAuditableEntity
{
    public DateTimeOffset CreatedAtUtc { get; internal set; }
    public Guid? CreatedByUserId { get; internal set; }
    public DateTimeOffset? ModifiedAtUtc { get; internal set; }
    public Guid? ModifiedByUserId { get; internal set; }
}
