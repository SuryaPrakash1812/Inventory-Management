namespace InventoryManagement.Domain.Enums;

/// <summary>
/// How much of a confirmed purchase has been paid. This stage only tracks
/// the status itself, set manually - a full payments/installments ledger
/// (linking specific payment transactions to purchases) is out of scope
/// here.
/// </summary>
public enum PurchasePaymentStatus
{
    Unpaid = 0,
    PartiallyPaid = 1,
    Paid = 2,
}
