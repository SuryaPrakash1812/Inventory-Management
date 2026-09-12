using InventoryManagement.Domain.Common;

namespace InventoryManagement.Domain.Entities;

/// <summary>
/// One product line on a <see cref="Purchase"/>. Line items belong entirely
/// to their parent document (they are created, edited, and deleted together
/// with it), so unlike the header they don't carry their own audit/soft-delete
/// trail - the Purchase's own audit fields already answer "when/by whom".
/// </summary>
public class PurchaseItem : BaseEntity
{
    public Guid PurchaseId { get; set; }

    public Purchase Purchase { get; set; } = null!;

    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public decimal Quantity { get; set; }

    public decimal UnitCost { get; set; }

    /// <summary>Quantity * UnitCost, stored (not computed) so historical lines are unaffected by later price changes.</summary>
    public decimal LineTotal { get; set; }
}
