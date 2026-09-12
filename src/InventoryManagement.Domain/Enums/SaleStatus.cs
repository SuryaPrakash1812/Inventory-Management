namespace InventoryManagement.Domain.Enums;

/// <summary>Lifecycle of a sale/order to a customer.</summary>
public enum SaleStatus
{
    Draft = 0,
    Invoiced = 1,
    Cancelled = 2,
}
