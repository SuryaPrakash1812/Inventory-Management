using InventoryManagement.Domain.Enums;

namespace InventoryManagement.Application.StockAdjustments;

public sealed record StockAdjustmentItemRequest(Guid ProductId, decimal QuantityChange, string? Notes);

/// <summary>Creates a new adjustment (StockAdjustmentId null) or replaces an existing Draft's items/header (StockAdjustmentId set) - never valid once Confirmed/Cancelled.</summary>
public sealed record SaveDraftStockAdjustmentRequest(
    Guid? StockAdjustmentId,
    DateTimeOffset AdjustmentDate,
    StockAdjustmentReason Reason,
    string? Notes,
    IReadOnlyList<StockAdjustmentItemRequest> Items);

public sealed record StockAdjustmentQueryParameters
{
    public string? SearchTerm { get; init; }
    public StockAdjustmentReason? Reason { get; init; }
    public StockAdjustmentStatus? Status { get; init; }
    public DateTimeOffset? FromDate { get; init; }
    public DateTimeOffset? ToDate { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public bool SortDescending { get; init; } = true;
}

public sealed record StockAdjustmentSummary(
    Guid Id,
    string AdjustmentNumber,
    DateTimeOffset AdjustmentDate,
    StockAdjustmentReason Reason,
    StockAdjustmentStatus Status,
    int ItemCount);

public sealed record StockAdjustmentItemDetail(
    Guid Id,
    Guid ProductId,
    string ProductSku,
    string ProductName,
    decimal QuantityBefore,
    decimal QuantityAfter,
    decimal QuantityChange,
    string? Notes);

public sealed record StockAdjustmentDetail(
    Guid Id,
    string AdjustmentNumber,
    DateTimeOffset AdjustmentDate,
    StockAdjustmentReason Reason,
    StockAdjustmentStatus Status,
    string? Notes,
    IReadOnlyList<StockAdjustmentItemDetail> Items);
