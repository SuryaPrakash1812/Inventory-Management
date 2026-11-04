using InventoryManagement.Domain.Enums;
using InventoryManagement.Domain.StockAdjustments;
using Xunit;

namespace InventoryManagement.Domain.Tests.StockAdjustments;

public class StockAdjustmentWorkflowTests
{
    [Fact]
    public void ValidateHasItems_FailsForZero()
    {
        Assert.Equal("At least one item is required.", StockAdjustmentWorkflow.ValidateHasItems(0).Error);
        Assert.True(StockAdjustmentWorkflow.ValidateHasItems(2).IsSuccess);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(-5, true)]
    [InlineData(0, false)]
    public void ValidateItemQuantityChange_RejectsOnlyZero(decimal change, bool expected)
    {
        Assert.Equal(expected, StockAdjustmentWorkflow.ValidateItemQuantityChange(change).IsSuccess);
    }

    [Theory]
    [InlineData(StockAdjustmentStatus.Confirmed)]
    [InlineData(StockAdjustmentStatus.Cancelled)]
    public void OnlyDrafts_CanBeEditedOrDeleted(StockAdjustmentStatus status)
    {
        Assert.True(StockAdjustmentWorkflow.EnsureEditable(status).IsFailure);
        Assert.True(StockAdjustmentWorkflow.EnsureDeletable(status).IsFailure);
        Assert.True(StockAdjustmentWorkflow.EnsureEditable(StockAdjustmentStatus.Draft).IsSuccess);
    }

    [Fact]
    public void EnsureConfirmable_IsIdempotent_AndRequiresItems()
    {
        Assert.Equal(
            "Stock adjustment is already Confirmed and cannot be confirmed again.",
            StockAdjustmentWorkflow.EnsureConfirmable(StockAdjustmentStatus.Confirmed, 1).Error);
        Assert.Equal(
            "Cannot confirm a stock adjustment with no items.",
            StockAdjustmentWorkflow.EnsureConfirmable(StockAdjustmentStatus.Draft, 0).Error);
        Assert.True(StockAdjustmentWorkflow.EnsureConfirmable(StockAdjustmentStatus.Draft, 1).IsSuccess);
    }

    [Fact]
    public void EnsureCancellable_RejectsAlreadyCancelled()
    {
        Assert.True(StockAdjustmentWorkflow.EnsureCancellable(StockAdjustmentStatus.Confirmed).IsSuccess);
        Assert.Equal(
            "Stock adjustment is already cancelled.",
            StockAdjustmentWorkflow.EnsureCancellable(StockAdjustmentStatus.Cancelled).Error);
    }

    [Theory]
    [InlineData(10, 10, true)]
    [InlineData(10, 4, true)]
    [InlineData(3, 4, false)]
    public void EnsureSufficientStockToReverse_NeverAllowsNegativeStock(decimal current, decimal originalIncrease, bool expected)
    {
        var result = StockAdjustmentWorkflow.EnsureSufficientStockToReverse(current, originalIncrease);

        Assert.Equal(expected, result.IsSuccess);
        if (!expected)
        {
            Assert.Contains($"only {current} remaining, needed {originalIncrease}", result.Error);
        }
    }

    [Theory]
    [InlineData(10, -10, true)]
    [InlineData(10, 5, true)]
    [InlineData(10, -11, false)]
    public void EnsureChangeKeepsStockNonNegative_RejectsOnlyNegativeResults(decimal current, decimal netChange, bool expected)
    {
        Assert.Equal(expected, StockAdjustmentWorkflow.EnsureChangeKeepsStockNonNegative(current, netChange).IsSuccess);
    }

    [Fact]
    public void FormatAdjustmentNumber_UsesAdjPrefix()
    {
        Assert.Equal("ADJ-XYZ789-00007", StockAdjustmentWorkflow.FormatAdjustmentNumber("XYZ789", 7));
    }
}
