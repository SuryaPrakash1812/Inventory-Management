namespace InventoryManagement.Domain.Enums;

/// <summary>
/// What kind of event caused a <see cref="Entities.StockMovement"/> ledger
/// entry. Every change to a product's on-hand quantity must be recorded as
/// exactly one of these, so the ledger can always explain "why" a balance
/// changed, not just "by how much".
/// </summary>
public enum StockMovementType
{
    OpeningBalance = 0,
    PurchaseReceipt = 1,
    SaleIssue = 2,
    AdjustmentIncrease = 3,
    AdjustmentDecrease = 4,
}
