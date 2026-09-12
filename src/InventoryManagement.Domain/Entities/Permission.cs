using InventoryManagement.Domain.Common;

namespace InventoryManagement.Domain.Entities;

/// <summary>
/// A single grantable capability (e.g. "Products.Edit"). This is static
/// reference data seeded by the database initializer, not something end
/// users create - hence <see cref="AuditableEntity"/> rather than the
/// soft-deletable base.
/// </summary>
public class Permission : AuditableEntity
{
    /// <summary>Stable machine name, e.g. "Products.Edit". Never shown to users directly.</summary>
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public ICollection<Role> Roles { get; set; } = new List<Role>();
}
