using InventoryManagement.Domain.Common;

namespace InventoryManagement.Domain.Entities;

public class Customer : AuditableSoftDeleteEntity
{
    public string Name { get; set; } = string.Empty;

    public string? ContactPerson { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Address { get; set; }

    /// <summary>GST/VAT/tax registration number, where applicable - format varies by jurisdiction, so this is a free-text field.</summary>
    public string? TaxId { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Sale> Sales { get; set; } = new List<Sale>();
}
