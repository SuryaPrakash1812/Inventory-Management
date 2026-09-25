using InventoryManagement.Domain.Enums;
using InventoryManagement.Domain.Purchases;
using Xunit;

namespace InventoryManagement.Domain.Tests.Purchases;

public class PurchaseWorkflowTests
{
    [Theory]
    [InlineData(0, 10, 0, 0, "Item quantity must be greater than zero.")]
    [InlineData(-1, 10, 0, 0, "Item quantity must be greater than zero.")]
    [InlineData(1, -1, 0, 0, "Item unit cost cannot be negative.")]
    [InlineData(1, 10, -1, 0, "Item discount cannot be negative.")]
    [InlineData(1, 10, 0, -1, "Item tax percentage must be between 0 and 100.")]
    [InlineData(1, 10, 0, 101, "Item tax percentage must be between 0 and 100.")]
    public void ValidateItemShape_RejectsInvalidValues_WithExactOriginalMessage(
        decimal quantity, decimal unitCost, decimal discountAmount, decimal taxPercentage, string expectedError)
    {
        var result = PurchaseWorkflow.ValidateItemShape(quantity, unitCost, discountAmount, taxPercentage);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedError, result.Error);
    }

    [Theory]
    [InlineData(1, 0, 0, 0)]
    [InlineData(1, 10, 0, 0)]
    [InlineData(1, 10, 0, 100)]
    [InlineData(0.5, 10, 0, 50)]
    public void ValidateItemShape_AcceptsValidValues(decimal quantity, decimal unitCost, decimal discountAmount, decimal taxPercentage)
    {
        var result = PurchaseWorkflow.ValidateItemShape(quantity, unitCost, discountAmount, taxPercentage);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateHasItems_FailsForZero()
    {
        var result = PurchaseWorkflow.ValidateHasItems(0);

        Assert.True(result.IsFailure);
        Assert.Equal("At least one item is required.", result.Error);
    }

    [Fact]
    public void ComputeLine_MatchesOriginalFormula()
    {
        // 2 units at 100 each = 200 subtotal, minus 20 discount = 180 taxable,
        // 10% tax = 18, line total = 198. Same numbers used in the original
        // manual testing session for this exact scenario.
        var result = PurchaseWorkflow.ComputeLine(quantity: 2, unitCost: 100, discountAmount: 20, taxPercentage: 10);

        Assert.Equal(200m, result.LineSubtotal);
        Assert.Equal(20m, result.DiscountAmount);
        Assert.Equal(18m, result.TaxAmount);
        Assert.Equal(198m, result.LineTotal);
    }

    [Fact]
    public void ComputeHeaderTotals_SumsAcrossMultipleLines()
    {
        var lines = new[]
        {
            PurchaseWorkflow.ComputeLine(2, 100, 20, 10),  // subtotal 200, discount 20, tax 18
            PurchaseWorkflow.ComputeLine(1, 50, 0, 0),      // subtotal 50, discount 0, tax 0
        };

        var totals = PurchaseWorkflow.ComputeHeaderTotals(lines);

        Assert.Equal(250m, totals.Subtotal);
        Assert.Equal(20m, totals.DiscountTotal);
        Assert.Equal(18m, totals.TaxTotal);
        Assert.Equal(248m, totals.TotalAmount); // 250 - 20 + 18
    }

    [Fact]
    public void EnsureEditable_SucceedsOnlyForDraft()
    {
        Assert.True(PurchaseWorkflow.EnsureEditable(PurchaseStatus.Draft).IsSuccess);
        Assert.True(PurchaseWorkflow.EnsureEditable(PurchaseStatus.Confirmed).IsFailure);
        Assert.True(PurchaseWorkflow.EnsureEditable(PurchaseStatus.Cancelled).IsFailure);
    }

    [Fact]
    public void EnsureDeletable_SucceedsOnlyForDraft_WithDistinctMessage()
    {
        var confirmedResult = PurchaseWorkflow.EnsureDeletable(PurchaseStatus.Confirmed);

        Assert.True(PurchaseWorkflow.EnsureDeletable(PurchaseStatus.Draft).IsSuccess);
        Assert.True(confirmedResult.IsFailure);
        Assert.Equal("Only a Draft purchase can be deleted - this purchase is Confirmed.", confirmedResult.Error);
    }

    [Fact]
    public void EnsureConfirmable_FailsIfNotDraft()
    {
        var result = PurchaseWorkflow.EnsureConfirmable(PurchaseStatus.Confirmed, itemCount: 3);

        Assert.True(result.IsFailure);
        Assert.Equal("Purchase is already Confirmed and cannot be confirmed again.", result.Error);
    }

    [Fact]
    public void EnsureConfirmable_FailsIfNoItems()
    {
        var result = PurchaseWorkflow.EnsureConfirmable(PurchaseStatus.Draft, itemCount: 0);

        Assert.True(result.IsFailure);
        Assert.Equal("Cannot confirm a purchase with no items.", result.Error);
    }

    [Fact]
    public void EnsureConfirmable_SucceedsForDraftWithItems()
    {
        var result = PurchaseWorkflow.EnsureConfirmable(PurchaseStatus.Draft, itemCount: 1);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void EnsureCancellable_FailsOnlyWhenAlreadyCancelled()
    {
        Assert.True(PurchaseWorkflow.EnsureCancellable(PurchaseStatus.Draft).IsSuccess);
        Assert.True(PurchaseWorkflow.EnsureCancellable(PurchaseStatus.Confirmed).IsSuccess);
        Assert.True(PurchaseWorkflow.EnsureCancellable(PurchaseStatus.Cancelled).IsFailure);
    }

    [Fact]
    public void EnsureSufficientStockToReverse_FailsWhenInsufficient()
    {
        var result = PurchaseWorkflow.EnsureSufficientStockToReverse(currentStock: 2, quantityToReverse: 5);

        Assert.True(result.IsFailure);
        Assert.Contains("only 2 of 5 remaining", result.Error);
    }

    [Fact]
    public void EnsureSufficientStockToReverse_SucceedsWhenExactlyEnough()
    {
        var result = PurchaseWorkflow.EnsureSufficientStockToReverse(currentStock: 5, quantityToReverse: 5);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void FormatPurchaseNumber_CombinesTagAndSequence()
    {
        var formatted = PurchaseWorkflow.FormatPurchaseNumber("A1B2C3", 7);

        Assert.Equal("PO-A1B2C3-00007", formatted);
    }

    [Fact]
    public void FormatPurchaseNumber_DifferentTags_NeverCollide_ForSameSequence()
    {
        // Decision 3's core guarantee: two offline installs generating
        // "purchase #1" independently must never produce the same string.
        var first = PurchaseWorkflow.FormatPurchaseNumber("AAAAAA", 1);
        var second = PurchaseWorkflow.FormatPurchaseNumber("BBBBBB", 1);

        Assert.NotEqual(first, second);
    }
}
