using InventoryManagement.Core.Common;
using InventoryManagement.Domain.Enums;

namespace InventoryManagement.Domain.Sales;

/// <summary>
/// Every Sale business rule that doesn't require touching a database.
/// Deliberately NOT a copy of PurchaseWorkflow - real differences exist:
///
/// - Status is Draft/Invoiced/Cancelled (SaleStatus), not
///   Draft/Confirmed/Cancelled - "confirm" in casual language is
///   "invoice" in this domain's actual terminology, and this class and
///   the service built on it use that real name throughout.
/// - SaleItem currently has no TaxPercentage/TaxAmount fields at all
///   (unlike PurchaseItem) - this was flagged as a gap back when the
///   Purchase module got its tax fields added and was never followed up
///   on. Adding them now would require a new SQLite migration for an
///   already-shipped entity; this implementation deliberately works with
///   the schema as it exists (LineTotal = Quantity * UnitPrice -
///   DiscountAmount, no tax term) rather than silently expanding scope
///   with an unplanned schema change. Documented as a known limitation in
///   the final report, not hidden.
/// - Stock moves in the OPPOSITE direction from Purchase: a Purchase
///   confirmation always increases stock (nothing to check); a Sale being
///   invoiced always DECREASES stock, which means - unlike Purchase.
///   Confirm - invoicing itself needs a sufficiency check up front,
///   mirroring the check Purchase only needed on Cancel (reversing an
///   increase).
/// </summary>
public static class SaleWorkflow
{
    public static Result ValidateItemShape(decimal quantity, decimal unitPrice, decimal discountAmount)
    {
        if (quantity <= 0)
        {
            return Result.Failure("Item quantity must be greater than zero.");
        }

        if (unitPrice < 0)
        {
            return Result.Failure("Item unit price cannot be negative.");
        }

        if (discountAmount < 0)
        {
            return Result.Failure("Item discount cannot be negative.");
        }

        return Result.Success();
    }

    public static Result ValidateHasItems(int itemCount) =>
        itemCount > 0 ? Result.Success() : Result.Failure("At least one item is required.");

    public readonly record struct LineComputation(decimal LineSubtotal, decimal DiscountAmount, decimal LineTotal);

    /// <summary>No tax term - see this class's own remarks on why SaleItem has no tax fields yet.</summary>
    public static LineComputation ComputeLine(decimal quantity, decimal unitPrice, decimal discountAmount)
    {
        var lineSubtotal = quantity * unitPrice;
        var lineTotal = lineSubtotal - discountAmount;
        return new LineComputation(lineSubtotal, discountAmount, lineTotal);
    }

    public readonly record struct HeaderTotals(decimal Subtotal, decimal DiscountTotal, decimal TaxTotal, decimal TotalAmount);

    /// <summary>TaxTotal is always 0 here (see class remarks) - kept as an explicit field anyway so Sale's header shape matches Purchase's and a future tax-fields migration can populate it without changing this method's signature.</summary>
    public static HeaderTotals ComputeHeaderTotals(IEnumerable<LineComputation> lines)
    {
        decimal subtotal = 0, discountTotal = 0;

        foreach (var line in lines)
        {
            subtotal += line.LineSubtotal;
            discountTotal += line.DiscountAmount;
        }

        return new HeaderTotals(subtotal, discountTotal, 0m, subtotal - discountTotal);
    }

    public static Result EnsureEditable(SaleStatus status) =>
        status == SaleStatus.Draft
            ? Result.Success()
            : Result.Failure($"Sale is {status} and can no longer be edited.");

    public static Result EnsureDeletable(SaleStatus status) =>
        status == SaleStatus.Draft
            ? Result.Success()
            : Result.Failure($"Only a Draft sale can be deleted - this sale is {status}.");

    /// <summary>The Status != Draft check is what makes invoicing idempotent.</summary>
    public static Result EnsureInvoiceable(SaleStatus status, int itemCount)
    {
        if (status != SaleStatus.Draft)
        {
            return Result.Failure($"Sale is already {status} and cannot be invoiced again.");
        }

        if (itemCount == 0)
        {
            return Result.Failure("Cannot invoice a sale with no items.");
        }

        return Result.Success();
    }

    public static Result EnsureCancellable(SaleStatus status) =>
        status != SaleStatus.Cancelled
            ? Result.Success()
            : Result.Failure("Sale is already cancelled.");

    /// <summary>Unlike Purchase.Confirm (which only ever adds stock), invoicing a Sale REMOVES stock - it must be checked up front, before touching any line, exactly the same "verify everything, then apply everything" shape Purchase uses for its Cancel-time reversal check.</summary>
    public static Result EnsureSufficientStockToIssue(decimal currentStock, decimal quantityToIssue) =>
        currentStock >= quantityToIssue
            ? Result.Success()
            : Result.Failure(
                $"Insufficient stock: only {currentStock} available, but {quantityToIssue} requested for one of the items. "
                    + "Reduce the quantity or restock before invoicing.");

    /// <summary>Same offline-safe numbering shape as Purchase/StockAdjustment, distinct prefix so document types are never confused. Used for BOTH SaleNumber (at creation) and InvoiceNumber (at invoicing) - the two are separate sequences.</summary>
    public static string FormatSaleNumber(string clientTag, int sequence) => $"SO-{clientTag}-{sequence:D5}";

    public static string FormatInvoiceNumber(string clientTag, int sequence) => $"INV-{clientTag}-{sequence:D5}";
}
