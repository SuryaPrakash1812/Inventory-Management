using InventoryManagement.Domain.Enums;

namespace InventoryManagement.Application.Inventory;

/// <summary>
/// Stock coming IN. MovementType must be one of the "increase" types
/// (OpeningBalance, PurchaseReceipt, SalesReturn) - AddStockAsync rejects
/// anything else, so a caller can't accidentally record a sale as an
/// addition.
/// </summary>
public sealed record AddStockRequest(
    Guid ProductId,
    decimal Quantity,
    StockMovementType MovementType,
    StockReferenceType ReferenceType,
    Guid ReferenceId,
    string? Notes = null);

/// <summary>
/// Stock going OUT. Quantity is a positive magnitude - the service applies
/// the negative sign internally, so callers never have to remember which
/// direction is "negative" for this method. MovementType must be one of the
/// "decrease" types (SaleIssue, PurchaseReturn).
/// </summary>
public sealed record RemoveStockRequest(
    Guid ProductId,
    decimal Quantity,
    StockMovementType MovementType,
    StockReferenceType ReferenceType,
    Guid ReferenceId,
    string? Notes = null,
    bool AllowNegativeStock = false);

/// <summary>
/// A manual correction. QuantityChange is signed (positive = found more
/// stock than expected, negative = found less) - the service infers
/// AdjustmentIncrease/AdjustmentDecrease from the sign, so the caller
/// doesn't pick the movement type directly for this one.
/// </summary>
public sealed record AdjustStockRequest(
    Guid ProductId,
    decimal QuantityChange,
    StockReferenceType ReferenceType,
    Guid ReferenceId,
    string? Notes = null,
    bool AllowNegativeStock = false);

public sealed record StockMovementResult(
    Guid MovementId,
    Guid ProductId,
    decimal QuantityChange,
    decimal QuantityBalanceAfter,
    DateTimeOffset OccurredAtUtc);

public sealed record StockHistoryQuery
{
    public Guid? ProductId { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

public sealed record StockHistoryEntry(
    Guid Id,
    Guid ProductId,
    string ProductSku,
    string ProductName,
    StockMovementType MovementType,
    decimal QuantityChange,
    decimal QuantityBalanceAfter,
    StockReferenceType ReferenceType,
    Guid ReferenceId,
    DateTimeOffset OccurredAtUtc,
    Guid? PerformedByUserId,
    string? Notes);

public sealed record LowStockProduct(
    Guid ProductId,
    string Sku,
    string Name,
    decimal CurrentStock,
    decimal MinimumStock);

public sealed record StockValuationResult(
    int ProductCount,
    decimal TotalCostValue,
    decimal TotalRetailValue);
