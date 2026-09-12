using InventoryManagement.Core.Common;

namespace InventoryManagement.Application.Categories;

public interface ICategoryService
{
    Task<IReadOnlyList<CategorySummary>> GetCategoriesAsync(
        string? searchTerm = null, CancellationToken cancellationToken = default);

    Task<Result<CategorySummary>> SaveCategoryAsync(
        SaveCategoryRequest request, CancellationToken cancellationToken = default);

    /// <summary>Fails if any (non-deactivated) product still references this category.</summary>
    Task<Result> DeleteCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);
}
