using InventoryManagement.Domain.Enums;

namespace InventoryManagement.Application.Purchases;

public sealed record PurchaseSummary(
    Guid Id,
    string PurchaseNumber,
    string? SupplierInvoiceNumber,
    Guid SupplierId,
    string SupplierName,
    DateTimeOffset PurchaseDate,
    PurchaseStatus Status,
    PurchasePaymentStatus PaymentStatus,
    decimal TotalAmount);

public sealed record PurchaseItemDetail(
    Guid Id,
    Guid ProductId,
    string ProductSku,
    string ProductName,
    decimal Quantity,
    decimal UnitCost,
    decimal DiscountAmount,
    decimal TaxPercentage,
    decimal TaxAmount,
    decimal LineTotal);

public sealed record PurchaseDetail(
    Guid Id,
    string PurchaseNumber,
    string? SupplierInvoiceNumber,
    Guid SupplierId,
    string SupplierName,
    DateTimeOffset PurchaseDate,
    PurchaseStatus Status,
    PurchasePaymentStatus PaymentStatus,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    string? Notes,
    IReadOnlyList<PurchaseItemDetail> Items);

/// <summary>One line as entered by the user - TaxAmount/LineTotal are computed server-side by SaveDraftAsync, not supplied by the caller.</summary>
public sealed record PurchaseItemRequest(
    Guid ProductId,
    decimal Quantity,
    decimal UnitCost,
    decimal DiscountAmount,
    decimal TaxPercentage);

/// <summary>Creates a new purchase (PurchaseId null) or replaces an existing Draft purchase's items/header (PurchaseId set) - never valid once Confirmed/Cancelled.</summary>
public sealed record SaveDraftPurchaseRequest(
    Guid? PurchaseId,
    Guid SupplierId,
    string? SupplierInvoiceNumber,
    DateTimeOffset PurchaseDate,
    string? Notes,
    IReadOnlyList<PurchaseItemRequest> Items);

public sealed record PurchaseQueryParameters
{
    /// <summary>Matched against PurchaseNumber and SupplierInvoiceNumber.</summary>
    public string? SearchTerm { get; init; }

    public Guid? SupplierId { get; init; }

    public PurchaseStatus? Status { get; init; }

    public DateTimeOffset? FromDate { get; init; }

    public DateTimeOffset? ToDate { get; init; }

    public bool SortDescending { get; init; } = true;

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}
