namespace InventoryManagement.Application.Suppliers;

public sealed record SupplierSummary(
    Guid Id,
    string Name,
    string? ContactPerson,
    string? Phone,
    string? Email,
    string? TaxId,
    bool IsActive);

public sealed record SupplierDetail(
    Guid Id,
    string Name,
    string? ContactPerson,
    string? Phone,
    string? Email,
    string? Address,
    string? TaxId,
    string? Notes,
    bool IsActive);

public sealed record CreateSupplierRequest(
    string Name,
    string? ContactPerson,
    string? Phone,
    string? Email,
    string? Address,
    string? TaxId,
    string? Notes);

public sealed record UpdateSupplierRequest(
    Guid SupplierId,
    string Name,
    string? ContactPerson,
    string? Phone,
    string? Email,
    string? Address,
    string? TaxId,
    string? Notes,
    bool IsActive);

public sealed record SupplierQueryParameters
{
    public string? SearchTerm { get; init; }
    public bool? IsActive { get; init; }
    public bool SortDescending { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}
