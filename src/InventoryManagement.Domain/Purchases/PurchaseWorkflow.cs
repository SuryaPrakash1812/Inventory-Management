using InventoryManagement.Core.Common;
using InventoryManagement.Domain.Enums;

namespace InventoryManagement.Domain.Purchases;

/// <summary>
/// Every Purchase business rule that doesn't require touching a database -
/// validation shapes, total computation, and workflow state-transition
/// guards. Deliberately pure (no I/O, no persistence types) so it can be
/// called identically from InventoryManagement.Infrastructure's local
/// SQLite implementation and from a future PostgreSQL-backed server
/// implementation, without either one re-implementing (and risking
/// drifting from) the same rules. Existence checks (does this supplier/
/// product actually exist) are NOT here on purpose - those inherently
/// require a specific data store to answer and stay in each
/// implementation's own service class.
///
/// Error messages here are copied verbatim from the original
/// SQLite-specific PurchaseService to preserve exact existing behavior -
/// this is an extraction, not a rewrite.
/// </summary>
public static class PurchaseWorkflow
{
    public static Result ValidateItemShape(decimal quantity, decimal unitCost, decimal discountAmount, decimal taxPercentage)
    {
        if (quantity <= 0)
        {
            return Result.Failure("Item quantity must be greater than zero.");
        }

        if (unitCost < 0)
        {
            return Result.Failure("Item unit cost cannot be negative.");
        }

        if (discountAmount < 0)
        {
            return Result.Failure("Item discount cannot be negative.");
        }

        if (taxPercentage is < 0 or > 100)
        {
            return Result.Failure("Item tax percentage must be between 0 and 100.");
        }

        return Result.Success();
    }

    public static Result ValidateHasItems(int itemCount) =>
        itemCount > 0 ? Result.Success() : Result.Failure("At least one item is required.");

    public readonly record struct LineComputation(decimal LineSubtotal, decimal DiscountAmount, decimal TaxAmount, decimal LineTotal);

    /// <summary>(Quantity * UnitCost - Discount) * Tax% / 100 for tax, and the resulting line total - same formula as the original inline computation in PurchaseService.SaveDraftAsync.</summary>
    public static LineComputation ComputeLine(decimal quantity, decimal unitCost, decimal discountAmount, decimal taxPercentage)
    {
        var lineSubtotal = quantity * unitCost;
        var taxableAmount = lineSubtotal - discountAmount;
        var taxAmount = taxableAmount * taxPercentage / 100m;
        var lineTotal = taxableAmount + taxAmount;

        return new LineComputation(lineSubtotal, discountAmount, taxAmount, lineTotal);
    }

    public readonly record struct HeaderTotals(decimal Subtotal, decimal DiscountTotal, decimal TaxTotal, decimal TotalAmount);

    public static HeaderTotals ComputeHeaderTotals(IEnumerable<LineComputation> lines)
    {
        decimal subtotal = 0, discountTotal = 0, taxTotal = 0;

        foreach (var line in lines)
        {
            subtotal += line.LineSubtotal;
            discountTotal += line.DiscountAmount;
            taxTotal += line.TaxAmount;
        }

        return new HeaderTotals(subtotal, discountTotal, taxTotal, subtotal - discountTotal + taxTotal);
    }

    /// <summary>Used by both SaveDraftAsync (editing an existing purchase) and DeleteDraftAsync - same underlying condition, kept as separate methods so each preserves its own original, distinct error message.</summary>
    public static Result EnsureEditable(PurchaseStatus status) =>
        status == PurchaseStatus.Draft
            ? Result.Success()
            : Result.Failure($"Purchase is {status} and can no longer be edited.");

    public static Result EnsureDeletable(PurchaseStatus status) =>
        status == PurchaseStatus.Draft
            ? Result.Success()
            : Result.Failure($"Only a Draft purchase can be deleted - this purchase is {status}.");

    /// <summary>The Status != Draft check here is what makes confirming idempotent - a second call on an already-Confirmed purchase always fails here before any stock is touched.</summary>
    public static Result EnsureConfirmable(PurchaseStatus status, int itemCount)
    {
        if (status != PurchaseStatus.Draft)
        {
            return Result.Failure($"Purchase is already {status} and cannot be confirmed again.");
        }

        if (itemCount == 0)
        {
            return Result.Failure("Cannot confirm a purchase with no items.");
        }

        return Result.Success();
    }

    public static Result EnsureCancellable(PurchaseStatus status) =>
        status != PurchaseStatus.Cancelled
            ? Result.Success()
            : Result.Failure("Purchase is already cancelled.");

    /// <summary>Safe business rule: never partially reverse stock or force it negative on cancellation - every line must have enough remaining stock to fully reverse.</summary>
    public static Result EnsureSufficientStockToReverse(decimal currentStock, decimal quantityToReverse) =>
        currentStock >= quantityToReverse
            ? Result.Success()
            : Result.Failure(
                "Cannot cancel: some of this purchase's stock has already been used elsewhere "
                    + $"(only {currentStock} of {quantityToReverse} remaining for one of the items). "
                    + "Reduce usage of the affected product(s) before cancelling.");

    /// <summary>
    /// Decision 3 (offline-safe purchase numbering): combines a short,
    /// installation-unique tag with a locally-scoped sequence number, so
    /// numbers generated on two different offline installs can never
    /// collide even before either syncs - without requiring any server
    /// coordination to generate (Decision 4: no API call to generate a
    /// purchase number). See PurchaseService.GenerateNextPurchaseNumberAsync
    /// for where the tag and sequence come from.
    /// </summary>
    public static string FormatPurchaseNumber(string clientTag, int sequence) => $"PO-{clientTag}-{sequence:D5}";

    /// <summary>
    /// The AUTHORITATIVE, server-side format - distinct from
    /// FormatPurchaseNumber above (which is the client's offline-safe
    /// interim scheme). Deliberately visually distinguishable at a glance
    /// (a 4-digit year instead of a 6-character hex tag) so a synced
    /// purchase's final number never looks like it could be confused with
    /// an unsynchronized local one. See
    /// PostgresSequencePurchaseNumberGenerator.GenerateAsync for where the
    /// sequence value itself comes from (a PostgreSQL SEQUENCE - see that
    /// method's remarks on why this must never be a local COUNT).
    /// </summary>
    public static string FormatServerPurchaseNumber(int year, long sequence) => $"PO-{year}-{sequence:D6}";
}
