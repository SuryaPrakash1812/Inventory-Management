using InventoryManagement.Domain.Common;

namespace InventoryManagement.Domain.Entities;

/// <summary>
/// A catalog item that can be bought, sold, and tracked in stock.
///
/// <see cref="QuantityOnHand"/> is a cached, denormalized value maintained
/// transactionally by inventory services (added in Stage 7) every time a
/// <see cref="StockMovement"/> is recorded - it exists purely so the UI can
/// show current stock without summing the entire movement ledger on every
/// screen. The ledger (<see cref="StockMovement"/>) is always the source of
/// truth; this column is a performance cache, never edited directly.
/// </summary>
public class Product : AuditableSoftDeleteEntity
{
    public string Sku { get; set; } = string.Empty;

    public string? Barcode { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Brand { get; set; }

    public Guid CategoryId { get; set; }

    public Category Category { get; set; } = null!;

    public Guid? PrimarySupplierId { get; set; }

    public Supplier? PrimarySupplier { get; set; }

    /// <summary>Unit of measure, e.g. "pcs", "kg", "box".</summary>
    public string Unit { get; set; } = "pcs";

    public decimal CostPrice { get; set; }

    public decimal SellingPrice { get; set; }

    /// <summary>Percentage (0-100) of tax applied to sales of this product.</summary>
    public decimal TaxPercentage { get; set; }

    /// <summary>Stock level below which the product should be reordered.</summary>
    public decimal ReorderLevel { get; set; }

    /// <summary>Cached current stock level - see class remarks.</summary>
    public decimal QuantityOnHand { get; set; }

    /// <summary>
    /// Whether the product is available for new purchases/sales. Distinct
    /// from <see cref="Domain.Common.ISoftDeletable.IsDeleted"/>: a product
    /// can be temporarily discontinued (IsActive = false) without being
    /// deleted at all.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public ICollection<PurchaseItem> PurchaseItems { get; set; } = new List<PurchaseItem>();

    public ICollection<SaleItem> SaleItems { get; set; } = new List<SaleItem>();

    public ICollection<StockMovement> StockMovements { get; set; } = new List<StockMovement>();

    public ICollection<StockAdjustmentItem> StockAdjustmentItems { get; set; } = new List<StockAdjustmentItem>();
}
