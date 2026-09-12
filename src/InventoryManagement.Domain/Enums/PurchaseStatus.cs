namespace InventoryManagement.Domain.Enums;

/// <summary>Lifecycle of a purchase order/invoice from a supplier.</summary>
public enum PurchaseStatus
{
    Draft = 0,
    Confirmed = 1,
    Received = 2,
    Cancelled = 3,
}
