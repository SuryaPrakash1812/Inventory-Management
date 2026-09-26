using InventoryManagement.Application.Products;

namespace InventoryManagement.App.ViewModels.Inventory;

/// <summary>
/// Wraps a ProductSummary with a computed display-only stock status. This
/// is NOT a new business rule - it mirrors the same threshold
/// IInventoryService.GetLowStockProductsAsync already uses server-side
/// (CurrentStock <= MinimumStock) purely for showing a status label in
/// this grid. The actual stock-mutation rules (negative-stock prevention,
/// movement creation) remain solely in InventoryService - this class
/// makes no decisions, it only labels a value that already came from
/// there.
/// </summary>
public sealed class InventoryOverviewRow
{
    public ProductSummary Product { get; }

    public string StockStatus =>
        Product.CurrentStock <= 0 ? "Out of Stock" :
        Product.CurrentStock <= Product.MinimumStock ? "Low Stock" : "In Stock";

    public decimal StockValueAtCost => Product.CurrentStock * Product.PurchasePrice;

    public InventoryOverviewRow(ProductSummary product)
    {
        Product = product;
    }
}
