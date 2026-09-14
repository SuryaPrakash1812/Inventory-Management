using InventoryManagement.Core.Common;
using InventoryManagement.Domain.Enums;

namespace InventoryManagement.Application.Purchases;

public interface IPurchaseService
{
    Task<PagedResult<PurchaseSummary>> GetPurchasesAsync(
        PurchaseQueryParameters query, CancellationToken cancellationToken = default);

    Task<PurchaseDetail?> GetPurchaseByIdAsync(Guid purchaseId, CancellationToken cancellationToken = default);

    /// <summary>Creates a new Draft (PurchaseId null) or replaces an existing Draft's header/items (PurchaseId set). Fails if the target purchase is not currently Draft.</summary>
    Task<Result<PurchaseDetail>> SaveDraftAsync(
        SaveDraftPurchaseRequest request, CancellationToken cancellationToken = default);

    /// <summary>Only a Draft purchase may be deleted - once Confirmed or Cancelled, a purchase is a permanent record.</summary>
    Task<Result> DeleteDraftAsync(Guid purchaseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Draft -> Confirmed. Creates one PurchaseReceipt stock movement per
    /// line item and updates each product's on-hand quantity. Fails (with
    /// no effect at all) if the purchase is not currently Draft - this is
    /// what makes the operation idempotent: calling it twice on the same
    /// purchase can only ever apply the stock increase once.
    /// </summary>
    Task<Result<PurchaseDetail>> ConfirmPurchaseAsync(Guid purchaseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Draft or Confirmed -> Cancelled. If the purchase was Confirmed, first
    /// verifies every line's product still has enough current stock to
    /// reverse the full originally-received quantity; if any line does not
    /// (because some of it was already sold/used elsewhere), the entire
    /// cancellation is rejected with no effect at all, rather than reversing
    /// only some lines or allowing stock to go negative. Cancelling an
    /// already-Cancelled purchase is rejected the same way Confirm is.
    /// </summary>
    Task<Result<PurchaseDetail>> CancelPurchaseAsync(Guid purchaseId, CancellationToken cancellationToken = default);

    Task<Result<PurchaseDetail>> SetPaymentStatusAsync(
        Guid purchaseId, PurchasePaymentStatus paymentStatus, CancellationToken cancellationToken = default);
}
