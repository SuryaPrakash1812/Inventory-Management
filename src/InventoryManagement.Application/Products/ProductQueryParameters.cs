namespace InventoryManagement.Application.Products;

/// <summary>
/// Everything needed to run one page of the product list query entirely on
/// the database side - no unfiltered "load everything then filter in
/// memory" allowed, since that's exactly what this stage's performance
/// requirement rules out.
/// </summary>
public sealed record ProductQueryParameters
{
    /// <summary>Matched against SKU, barcode, and name (case-insensitive, partial match).</summary>
    public string? SearchTerm { get; init; }

    public Guid? CategoryId { get; init; }

    /// <summary>null = both active and inactive, true = active only, false = inactive only.</summary>
    public bool? IsActive { get; init; }

    public ProductSortColumn SortColumn { get; init; } = ProductSortColumn.Name;

    public bool SortDescending { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}
