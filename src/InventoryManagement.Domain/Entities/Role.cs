using InventoryManagement.Domain.Common;

namespace InventoryManagement.Domain.Entities;

/// <summary>
/// A named set of <see cref="Permission"/>s that can be assigned to
/// <see cref="User"/>s. "Administrator" and "Standard User" are seeded as
/// starting points (see DatabaseInitializer); administrators can create
/// additional roles once the Users feature (Stage 9) has a UI for it.
/// </summary>
public class Role : AuditableSoftDeleteEntity
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public ICollection<Permission> Permissions { get; set; } = new List<Permission>();

    public ICollection<User> Users { get; set; } = new List<User>();
}
