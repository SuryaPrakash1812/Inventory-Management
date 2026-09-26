using InventoryManagement.Core.Common;

namespace InventoryManagement.Application.StockAdjustments;

public interface IStockAdjustmentService
{
    Task<PagedResult<StockAdjustmentSummary>> GetStockAdjustmentsAsync(
        StockAdjustmentQueryParameters query, CancellationToken cancellationToken = default);

    Task<StockAdjustmentDetail?> GetStockAdjustmentByIdAsync(
        Guid stockAdjustmentId, CancellationToken cancellationToken = default);

    /// <summary>Creates a new Draft (StockAdjustmentId null) or replaces an existing Draft's header/items (StockAdjustmentId set). Fails if the target adjustment is not currently Draft.</summary>
    Task<Result<StockAdjustmentDetail>> SaveDraftAsync(
        SaveDraftStockAdjustmentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Only a Draft stock adjustment may be deleted - once Confirmed or Cancelled, it is a permanent record.</summary>
    Task<Result> DeleteDraftAsync(Guid stockAdjustmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Draft -> Confirmed. Applies each item's QuantityChange via
    /// IInventoryService.AdjustStockAsync, producing an AdjustmentIncrease
    /// or AdjustmentDecrease stock movement per line. Fails (with no
    /// effect at all) if the adjustment is not currently Draft - this is
    /// what makes the operation idempotent.
    /// </summary>
    Task<Result<StockAdjustmentDetail>> ConfirmAsync(Guid stockAdjustmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Draft or Confirmed -> Cancelled. If the adjustment was Confirmed,
    /// first verifies every line's product still has enough current stock
    /// to reverse the full original change; if any line does not, the
    /// entire cancellation is rejected with no effect at all.
    /// </summary>
    Task<Result<StockAdjustmentDetail>> CancelAsync(Guid stockAdjustmentId, CancellationToken cancellationToken = default);
}
