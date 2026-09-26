using InventoryManagement.Core.Common;
using InventoryManagement.Domain.Enums;

namespace InventoryManagement.Domain.StockAdjustments;

/// <summary>
/// Every Stock Adjustment business rule that doesn't require touching a
/// database - mirrors InventoryManagement.Domain.Purchases.PurchaseWorkflow's
/// shape closely, since StockAdjustment shares the exact same Draft ->
/// Confirmed -> Cancelled status model as Purchase. Existence checks (does
/// this product actually exist) are NOT here on purpose - those inherently
/// require a specific data store to answer and stay in the calling
/// service.
/// </summary>
public static class StockAdjustmentWorkflow
{
    public static Result ValidateHasItems(int itemCount) =>
        itemCount > 0 ? Result.Success() : Result.Failure("At least one item is required.");

    /// <summary>A zero-quantity adjustment is meaningless - it must genuinely increase or decrease something.</summary>
    public static Result ValidateItemQuantityChange(decimal quantityChange) =>
        quantityChange != 0
            ? Result.Success()
            : Result.Failure("Adjustment quantity must be a nonzero increase or decrease.");

    public static Result EnsureEditable(StockAdjustmentStatus status) =>
        status == StockAdjustmentStatus.Draft
            ? Result.Success()
            : Result.Failure($"Stock adjustment is {status} and can no longer be edited.");

    public static Result EnsureDeletable(StockAdjustmentStatus status) =>
        status == StockAdjustmentStatus.Draft
            ? Result.Success()
            : Result.Failure($"Only a Draft stock adjustment can be deleted - this one is {status}.");

    /// <summary>The Status != Draft check is what makes confirming idempotent - a second call on an already-Confirmed adjustment always fails here before any stock is touched.</summary>
    public static Result EnsureConfirmable(StockAdjustmentStatus status, int itemCount)
    {
        if (status != StockAdjustmentStatus.Draft)
        {
            return Result.Failure($"Stock adjustment is already {status} and cannot be confirmed again.");
        }

        if (itemCount == 0)
        {
            return Result.Failure("Cannot confirm a stock adjustment with no items.");
        }

        return Result.Success();
    }

    public static Result EnsureCancellable(StockAdjustmentStatus status) =>
        status != StockAdjustmentStatus.Cancelled
            ? Result.Success()
            : Result.Failure("Stock adjustment is already cancelled.");

    /// <summary>Safe business rule: never let reversing a confirmed increase force stock negative - mirrors PurchaseWorkflow.EnsureSufficientStockToReverse.</summary>
    public static Result EnsureSufficientStockToReverse(decimal currentStock, decimal originalIncreaseQuantity) =>
        currentStock >= originalIncreaseQuantity
            ? Result.Success()
            : Result.Failure(
                "Cannot cancel: some of this adjustment's added stock has already been used elsewhere "
                    + $"(only {currentStock} remaining, needed {originalIncreaseQuantity} to fully reverse). "
                    + "Reduce usage of the affected product(s) before cancelling.");

    /// <summary>Same collision-safe, offline-only numbering shape as PurchaseWorkflow.FormatPurchaseNumber - a different prefix so the two document types are never confused.</summary>
    public static string FormatAdjustmentNumber(string clientTag, int sequence) => $"ADJ-{clientTag}-{sequence:D5}";
}
