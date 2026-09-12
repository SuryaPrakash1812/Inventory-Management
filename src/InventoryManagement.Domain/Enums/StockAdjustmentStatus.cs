namespace InventoryManagement.Domain.Enums;

/// <summary>Lifecycle of a stock adjustment document.</summary>
public enum StockAdjustmentStatus
{
    Draft = 0,
    Confirmed = 1,
    Cancelled = 2,
}
