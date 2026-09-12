using InventoryManagement.Domain.Common;

namespace InventoryManagement.Domain.Entities;

public class Supplier : AuditableSoftDeleteEntity
{
    public string Name { get; set; } = string.Empty;

    public string? ContactPerson { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Address { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Product> SuppliedProducts { get; set; } = new List<Product>();

    public ICollection<Purchase> Purchases { get; set; } = new List<Purchase>();
}
