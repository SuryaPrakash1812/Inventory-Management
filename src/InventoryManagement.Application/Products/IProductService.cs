using InventoryManagement.Core.Common;

namespace InventoryManagement.Application.Products;

public interface IProductService
{
    Task<PagedResult<ProductSummary>> GetProductsAsync(
        ProductQueryParameters query, CancellationToken cancellationToken = default);

    Task<ProductDetail?> GetProductByIdAsync(Guid productId, CancellationToken cancellationToken = default);

    Task<Result<ProductSummary>> CreateProductAsync(
        CreateProductRequest request, CancellationToken cancellationToken = default);

    Task<Result<ProductSummary>> UpdateProductAsync(
        UpdateProductRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sets IsActive to false. Reversible via UpdateProductAsync - never a hard delete, and never touches the soft-delete flag (see Product.IsActive remarks).</summary>
    Task<Result> DeactivateProductAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>Exports every product matching the given filter (ignoring paging) as CSV text.</summary>
    Task<string> ExportToCsvAsync(ProductQueryParameters query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports products from CSV text. A row whose SKU matches an existing
    /// product updates it (except stock, which import never touches for an
    /// existing product); otherwise a new product is created. Unknown
    /// category names fail that row rather than silently creating a category.
    /// </summary>
    Task<ProductImportResult> ImportFromCsvAsync(string csvContent, CancellationToken cancellationToken = default);
}
