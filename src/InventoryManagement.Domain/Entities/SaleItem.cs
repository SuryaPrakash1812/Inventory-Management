using InventoryManagement.Domain.Common;

namespace InventoryManagement.Domain.Entities;

/// <summary>One product line on a <see cref="Sale"/>. See <see cref="PurchaseItem"/> remarks re: no separate audit trail.</summary>
public class SaleItem : BaseEntity
{
    public Guid SaleId { get; set; }

    public Sale Sale { get; set; } = null!;

    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal DiscountAmount { get; set; }

    /// <summary>(Quantity * UnitPrice) - DiscountAmount, stored so historical lines are unaffected by later price changes.</summary>
    public decimal LineTotal { get; set; }
}
