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
    PurchaseReturn = 5,
    SalesReturn = 6,

    /// <summary>
    /// Reserved for a future multi-location feature. No InventoryService
    /// method produces this yet - there is only one stock location today,
    /// so a "transfer" has nowhere to go. Included now so the type exists
    /// when that feature is built, per the Stage 6 spec's "if supported
    /// later".
    /// </summary>
    StockTransfer = 7,
}
