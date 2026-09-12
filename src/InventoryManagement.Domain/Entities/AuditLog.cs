using InventoryManagement.Domain.Common;
using InventoryManagement.Domain.Enums;

namespace InventoryManagement.Domain.Entities;

/// <summary>
/// An append-only record of a significant action taken in the system (an
/// entity created/updated/deleted, a login, etc.). Like <see cref="StockMovement"/>,
/// rows here are never edited - a correction is a new row, not a rewrite -
/// so this extends <see cref="BaseEntity"/> directly.
///
/// Stage 2 only creates the table/shape; nothing writes to it automatically
/// yet. Populating it (via a SaveChanges interceptor once a "current user"
/// concept exists) is Stage 9's job, once there is a real acting user to
/// attribute actions to.
/// </summary>
public class AuditLog : BaseEntity
{
    public DateTimeOffset OccurredAtUtc { get; set; }

    public Guid? UserId { get; set; }

    public AuditAction Action { get; set; }

    /// <summary>Name of the affected entity type, e.g. "Product".</summary>
    public string EntityName { get; set; } = string.Empty;

    /// <summary>Id of the affected row. Not a database FK - it can point at any table.</summary>
    public Guid? EntityId { get; set; }

    /// <summary>Human-readable summary or serialized detail of what changed.</summary>
    public string? Details { get; set; }
}
