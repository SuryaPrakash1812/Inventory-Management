using InventoryManagement.Domain.Enums;

namespace InventoryManagement.Application.Sales;

public sealed record SaleItemRequest(Guid ProductId, decimal Quantity, decimal UnitPrice, decimal DiscountAmount);

public sealed record SaveDraftSaleRequest(
    Guid? SaleId,
    Guid CustomerId,
    DateTimeOffset SaleDate,
    string? Notes,
    IReadOnlyList<SaleItemRequest> Items);

public sealed record SaleQueryParameters
{
    public string? SearchTerm { get; init; }
    public Guid? CustomerId { get; init; }
    public SaleStatus? Status { get; init; }
    public DateTimeOffset? FromDate { get; init; }
    public DateTimeOffset? ToDate { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public bool SortDescending { get; init; } = true;
}

public sealed record SaleSummary(
    Guid Id,
    string SaleNumber,
    string? InvoiceNumber,
    string CustomerName,
    DateTimeOffset SaleDate,
    SaleStatus Status,
    decimal TotalAmount);

public sealed record SaleItemDetail(
    Guid Id,
    Guid ProductId,
    string ProductSku,
    string ProductName,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal LineTotal);

public sealed record SaleDetail(
    Guid Id,
    string SaleNumber,
    string? InvoiceNumber,
    Guid CustomerId,
    string CustomerName,
    DateTimeOffset SaleDate,
    SaleStatus Status,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    string? Notes,
    IReadOnlyList<SaleItemDetail> Items);
