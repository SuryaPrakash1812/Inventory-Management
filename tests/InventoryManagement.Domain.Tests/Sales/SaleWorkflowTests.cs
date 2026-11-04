using InventoryManagement.Domain.Enums;
using InventoryManagement.Domain.Sales;
using Xunit;

namespace InventoryManagement.Domain.Tests.Sales;

public class SaleWorkflowTests
{
    [Theory]
    [InlineData(0, 10, 0, "Item quantity must be greater than zero.")]
    [InlineData(-1, 10, 0, "Item quantity must be greater than zero.")]
    [InlineData(1, -1, 0, "Item unit price cannot be negative.")]
    [InlineData(1, 10, -1, "Item discount cannot be negative.")]
    public void ValidateItemShape_RejectsInvalidValues(decimal quantity, decimal unitPrice, decimal discount, string expectedError)
    {
        var result = SaleWorkflow.ValidateItemShape(quantity, unitPrice, discount);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedError, result.Error);
    }

    [Fact]
    public void ValidateItemShape_RejectsDiscountAboveLineAmount_ButAllowsFullDiscount()
    {
        Assert.True(SaleWorkflow.ValidateItemShape(2, 5, 11).IsFailure);
        Assert.True(SaleWorkflow.ValidateItemShape(2, 5, 10).IsSuccess);
    }

    [Fact]
    public void ValidateItemShape_AcceptsFreeItemWithNoDiscount()
    {
        Assert.True(SaleWorkflow.ValidateItemShape(1, 0, 0).IsSuccess);
    }

    [Fact]
    public void ValidateHasItems_FailsForZero()
    {
        Assert.Equal("At least one item is required.", SaleWorkflow.ValidateHasItems(0).Error);
        Assert.True(SaleWorkflow.ValidateHasItems(1).IsSuccess);
    }

    [Fact]
    public void ComputeLine_AndHeaderTotals_SubtractDiscountsWithNoTax()
    {
        var first = SaleWorkflow.ComputeLine(quantity: 2, unitPrice: 50, discountAmount: 10);
        var second = SaleWorkflow.ComputeLine(quantity: 1.5m, unitPrice: 20, discountAmount: 0);

        Assert.Equal(100m, first.LineSubtotal);
        Assert.Equal(90m, first.LineTotal);
        Assert.Equal(30m, second.LineTotal);

        var totals = SaleWorkflow.ComputeHeaderTotals(new[] { first, second });

        Assert.Equal(130m, totals.Subtotal);
        Assert.Equal(10m, totals.DiscountTotal);
        Assert.Equal(0m, totals.TaxTotal);
        Assert.Equal(120m, totals.TotalAmount);
    }

    [Theory]
    [InlineData(SaleStatus.Invoiced)]
    [InlineData(SaleStatus.Cancelled)]
    public void OnlyDrafts_CanBeEditedOrDeleted(SaleStatus status)
    {
        Assert.True(SaleWorkflow.EnsureEditable(status).IsFailure);
        Assert.True(SaleWorkflow.EnsureDeletable(status).IsFailure);
        Assert.True(SaleWorkflow.EnsureEditable(SaleStatus.Draft).IsSuccess);
        Assert.True(SaleWorkflow.EnsureDeletable(SaleStatus.Draft).IsSuccess);
    }

    [Fact]
    public void EnsureInvoiceable_RejectsSecondInvoice_AndEmptySale()
    {
        Assert.Equal(
            "Sale is already Invoiced and cannot be invoiced again.",
            SaleWorkflow.EnsureInvoiceable(SaleStatus.Invoiced, itemCount: 3).Error);
        Assert.Equal("Cannot invoice a sale with no items.", SaleWorkflow.EnsureInvoiceable(SaleStatus.Draft, 0).Error);
        Assert.True(SaleWorkflow.EnsureInvoiceable(SaleStatus.Draft, 1).IsSuccess);
    }

    [Fact]
    public void EnsureCancellable_AllowsDraftAndInvoiced_ButNotCancelled()
    {
        Assert.True(SaleWorkflow.EnsureCancellable(SaleStatus.Draft).IsSuccess);
        Assert.True(SaleWorkflow.EnsureCancellable(SaleStatus.Invoiced).IsSuccess);
        Assert.Equal("Sale is already cancelled.", SaleWorkflow.EnsureCancellable(SaleStatus.Cancelled).Error);
    }

    [Theory]
    [InlineData(10, 10, true)]
    [InlineData(10, 9.5, true)]
    [InlineData(10, 10.01, false)]
    [InlineData(0, 1, false)]
    public void EnsureSufficientStockToIssue_RequiresEnoughStock(decimal current, decimal requested, bool expected)
    {
        Assert.Equal(expected, SaleWorkflow.EnsureSufficientStockToIssue(current, requested).IsSuccess);
    }

    [Fact]
    public void DocumentNumbers_UseDistinctPrefixes()
    {
        Assert.Equal("SO-ABC123-00042", SaleWorkflow.FormatSaleNumber("ABC123", 42));
        Assert.Equal("INV-ABC123-00042", SaleWorkflow.FormatInvoiceNumber("ABC123", 42));
    }
}
