namespace InventoryManagement.Domain.Enums;

/// <summary>Why a manual stock adjustment was made.</summary>
public enum StockAdjustmentReason
{
    Damaged = 0,
    Lost = 1,
    Found = 2,
    Expired = 3,
    Correction = 4,
    Other = 5,
}
