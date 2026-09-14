namespace InventoryManagement.Domain.Enums;

/// <summary>
/// Lifecycle of a purchase order/invoice from a supplier. Confirming a
/// purchase is the receiving event itself - there is no separate "received"
/// state - so this is a strict three-state workflow: Draft is freely
/// editable and has no stock impact; Confirmed creates the stock movements
/// and is the point of no return for editing line items; Cancelled reverses
/// those movements (if any) and ends the workflow. Both Confirm and Cancel
/// are one-way transitions guarded so they can only fire once - see
/// PurchaseService remarks on why that is what makes them idempotent.
/// </summary>
public enum PurchaseStatus
{
    Draft = 0,
    Confirmed = 1,
    Cancelled = 2,
}
