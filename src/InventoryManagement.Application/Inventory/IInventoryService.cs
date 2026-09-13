using InventoryManagement.Core.Common;

namespace InventoryManagement.Application.Inventory;

/// <summary>
/// The single centralized service responsible for every change to a
/// product's stock quantity. Nothing else in the application should ever
/// write to Product.QuantityOnHand or insert a StockMovement row directly -
/// every stock-affecting feature (Purchases, Sales, Returns, Adjustments,
/// in later stages) goes through this interface, so there is exactly one
/// place that enforces negative-stock rules, stamps who/when, and keeps the
/// ledger and the cached balance in sync.
///
/// Exception: a brand-new product's own opening balance, set at the moment
/// the product row itself is created (see ProductService), is not routed
/// through here - there is no existing balance to race against for a row
/// that doesn't exist yet, so that one bootstrap case is safe to handle
/// directly. Every change to an EXISTING product must come through here.
/// </summary>
public interface IInventoryService
{
    Task<decimal> GetCurrentStockAsync(Guid productId, CancellationToken cancellationToken = default);

    Task<Result<StockMovementResult>> AddStockAsync(
        AddStockRequest request, CancellationToken cancellationToken = default);

    Task<Result<StockMovementResult>> RemoveStockAsync(
        RemoveStockRequest request, CancellationToken cancellationToken = default);

    Task<Result<StockMovementResult>> AdjustStockAsync(
        AdjustStockRequest request, CancellationToken cancellationToken = default);

    Task<PagedResult<StockHistoryEntry>> GetStockHistoryAsync(
        StockHistoryQuery query, CancellationToken cancellationToken = default);

    /// <summary>Active products whose current stock is at or below their configured minimum (reorder) level.</summary>
    Task<IReadOnlyList<LowStockProduct>> GetLowStockProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>Total value of all active products' current stock, at both cost and selling price.</summary>
    Task<StockValuationResult> GetStockValuationAsync(CancellationToken cancellationToken = default);
}
