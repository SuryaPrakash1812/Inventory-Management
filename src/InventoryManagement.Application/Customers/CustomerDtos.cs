namespace InventoryManagement.Application.Customers;

public sealed record CustomerSummary(
    Guid Id,
    string Name,
    string? Phone,
    string? Email,
    string? TaxId,
    bool IsActive);

public sealed record CustomerDetail(
    Guid Id,
    string Name,
    string? ContactPerson,
    string? Phone,
    string? Email,
    string? Address,
    string? TaxId,
    string? Notes,
    bool IsActive);

public sealed record CreateCustomerRequest(
    string Name,
    string? ContactPerson,
    string? Phone,
    string? Email,
    string? Address,
    string? TaxId,
    string? Notes);

public sealed record UpdateCustomerRequest(
    Guid CustomerId,
    string Name,
    string? ContactPerson,
    string? Phone,
    string? Email,
    string? Address,
    string? TaxId,
    string? Notes,
    bool IsActive);

public sealed record CustomerQueryParameters
{
    public string? SearchTerm { get; init; }
    public bool? IsActive { get; init; }
    public bool SortDescending { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}
