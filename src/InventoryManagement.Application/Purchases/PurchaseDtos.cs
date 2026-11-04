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

/// <summary>What cancelling will do to one product's stock - quantities are totalled across all of the purchase's lines for that product.</summary>
public sealed record PurchaseCancellationLine(
    Guid ProductId,
    string ProductSku,
    string ProductName,
    decimal QuantityToReverse,
    decimal CurrentStock)
{
    public decimal StockAfter => CurrentStock - QuantityToReverse;

    public bool CanReverse => StockAfter >= 0;
}

/// <summary>
/// The effect of cancelling a purchase, worked out before anything changes,
/// using the same rule CancelPurchaseAsync enforces. Lines is empty when no
/// stock is affected (a Draft never added any).
/// </summary>
public sealed record PurchaseCancellationPreview(
    Guid PurchaseId,
    string PurchaseNumber,
    PurchaseStatus Status,
    PurchasePaymentStatus PaymentStatus,
    decimal TotalAmount,
    IReadOnlyList<PurchaseCancellationLine> Lines)
{
    public bool CanCancel => Lines.All(l => l.CanReverse);
}

public sealed record PurchaseQueryParameters
{
    /// <summary>Matched against PurchaseNumber, SupplierInvoiceNumber and the supplier's name.</summary>
    public string? SearchTerm { get; init; }

    public Guid? SupplierId { get; init; }

    public PurchaseStatus? Status { get; init; }

    public DateTimeOffset? FromDate { get; init; }

    public DateTimeOffset? ToDate { get; init; }

    public bool SortDescending { get; init; } = true;

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}
