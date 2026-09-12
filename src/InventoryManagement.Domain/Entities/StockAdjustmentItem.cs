using InventoryManagement.Domain.Common;

namespace InventoryManagement.Domain.Entities;

/// <summary>One product line on a <see cref="StockAdjustment"/>.</summary>
public class StockAdjustmentItem : BaseEntity
{
    public Guid StockAdjustmentId { get; set; }

    public StockAdjustment StockAdjustment { get; set; } = null!;

    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public decimal QuantityBefore { get; set; }

    public decimal QuantityAfter { get; set; }

    /// <summary>QuantityAfter - QuantityBefore, stored for convenience even though it is derivable.</summary>
    public decimal QuantityChange { get; set; }

    public string? Notes { get; set; }
}
