namespace InventoryManagement.Application.Products;

public sealed record ProductImportRowError(int RowNumber, string Message);

public sealed record ProductImportResult(
    int CreatedCount,
    int UpdatedCount,
    IReadOnlyList<ProductImportRowError> Errors)
{
    public int TotalSucceeded => CreatedCount + UpdatedCount;
}
