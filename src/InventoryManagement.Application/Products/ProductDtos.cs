namespace InventoryManagement.Application.Products;

public sealed record ProductSummary(
    Guid Id,
    string Sku,
    string? Barcode,
    string Name,
    string? Brand,
    Guid CategoryId,
    string CategoryName,
    string Unit,
    decimal PurchasePrice,
    decimal SellingPrice,
    decimal TaxPercentage,
    decimal MinimumStock,
    decimal CurrentStock,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ModifiedAtUtc);

public sealed record ProductDetail(
    Guid Id,
    string Sku,
    string? Barcode,
    string Name,
    string? Description,
    string? Brand,
    Guid CategoryId,
    string CategoryName,
    string Unit,
    decimal PurchasePrice,
    decimal SellingPrice,
    decimal TaxPercentage,
    decimal MinimumStock,
    decimal CurrentStock,
    bool IsActive);

/// <param name="InitialStock">
/// Opening stock balance for a brand-new product. Not present on
/// UpdateProductRequest - once a product exists, its stock can only change
/// through Purchases/Sales/Adjustments (later stages), never by directly
/// editing the product record.
/// </param>
public sealed record CreateProductRequest(
    string Sku,
    string? Barcode,
    string Name,
    string? Description,
    string? Brand,
    Guid CategoryId,
    string Unit,
    decimal PurchasePrice,
    decimal SellingPrice,
    decimal TaxPercentage,
    decimal MinimumStock,
    decimal InitialStock);

public sealed record UpdateProductRequest(
    Guid ProductId,
    string? Barcode,
    string Name,
    string? Description,
    string? Brand,
    Guid CategoryId,
    string Unit,
    decimal PurchasePrice,
    decimal SellingPrice,
    decimal TaxPercentage,
    decimal MinimumStock,
    bool IsActive);
