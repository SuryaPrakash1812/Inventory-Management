namespace InventoryManagement.Application.Categories;

public sealed record CategorySummary(
    Guid Id,
    string Name,
    string? Description,
    Guid? ParentCategoryId,
    string? ParentCategoryName,
    int ProductCount);

public sealed record SaveCategoryRequest(
    Guid? CategoryId,
    string Name,
    string? Description,
    Guid? ParentCategoryId);
