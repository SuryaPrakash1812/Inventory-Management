using InventoryManagement.Domain.Common;
using InventoryManagement.Domain.Enums;

namespace InventoryManagement.Domain.Entities;

/// <summary>
/// A manual stock correction document (damage, loss, recount, etc.). Once
/// confirmed, each of its <see cref="Items"/> produces an offsetting
/// <see cref="StockMovement"/> - the adjustment is the "why", the movement
/// is the permanent ledger entry.
/// </summary>
public class StockAdjustment : AuditableSoftDeleteEntity
{
    public string AdjustmentNumber { get; set; } = string.Empty;

    public DateTimeOffset AdjustmentDate { get; set; }

    public StockAdjustmentReason Reason { get; set; }

    public StockAdjustmentStatus Status { get; set; } = StockAdjustmentStatus.Draft;

    public string? Notes { get; set; }

    public ICollection<StockAdjustmentItem> Items { get; set; } = new List<StockAdjustmentItem>();
}
