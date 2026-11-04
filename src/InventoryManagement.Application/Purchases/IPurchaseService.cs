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
    /// verifies each product still has enough current stock to reverse the
    /// full quantity received across all lines; if any does not (because some
    /// of it was already sold/used elsewhere), the entire cancellation is
    /// rejected with no effect at all, rather than reversing only some lines
    /// or allowing stock to go negative. All stock movements and the status
    /// change commit together. Cancelling an already-Cancelled purchase is
    /// rejected the same way Confirm is. An optional reason is added to the
    /// purchase's notes.
    /// </summary>
    Task<Result<PurchaseDetail>> CancelPurchaseAsync(
        Guid purchaseId, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>What cancelling would do, without changing anything. Fails if the purchase does not exist or is already cancelled.</summary>
    Task<Result<PurchaseCancellationPreview>> PreviewCancellationAsync(
        Guid purchaseId, CancellationToken cancellationToken = default);

    /// <summary>Only a Confirmed purchase - see PurchaseWorkflow.EnsurePaymentStatusChangeable.</summary>
    Task<Result<PurchaseDetail>> SetPaymentStatusAsync(
        Guid purchaseId, PurchasePaymentStatus paymentStatus, CancellationToken cancellationToken = default);
}
