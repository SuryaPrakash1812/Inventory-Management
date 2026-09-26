using InventoryManagement.Core.Common;

namespace InventoryManagement.Application.Sales;

public interface ISaleService
{
    Task<PagedResult<SaleSummary>> GetSalesAsync(SaleQueryParameters query, CancellationToken cancellationToken = default);

    Task<SaleDetail?> GetSaleByIdAsync(Guid saleId, CancellationToken cancellationToken = default);

    /// <summary>Creates a new Draft (SaleId null) or replaces an existing Draft's header/items (SaleId set). Fails if the target sale is not currently Draft.</summary>
    Task<Result<SaleDetail>> SaveDraftAsync(SaveDraftSaleRequest request, CancellationToken cancellationToken = default);

    /// <summary>Only a Draft sale may be deleted.</summary>
    Task<Result> DeleteDraftAsync(Guid saleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Draft -> Invoiced. Validates sufficient stock for every line BEFORE
    /// touching any of them, then reduces stock via
    /// IInventoryService.RemoveStockAsync per line (SaleIssue movement),
    /// and assigns the sale's InvoiceNumber - a separate document number
    /// from SaleNumber, only assigned at this point.
    /// </summary>
    Task<Result<SaleDetail>> InvoiceAsync(Guid saleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Draft or Invoiced -> Cancelled. If the sale was Invoiced, restores
    /// stock via IInventoryService.AddStockAsync per line (SalesReturn
    /// movement) - this can never fail on a stock-sufficiency basis
    /// (adding stock back can't go negative), unlike Purchase's
    /// equivalent reversal.
    /// </summary>
    Task<Result<SaleDetail>> CancelAsync(Guid saleId, CancellationToken cancellationToken = default);
}
