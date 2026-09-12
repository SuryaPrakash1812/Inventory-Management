using InventoryManagement.Application.Auth;
using InventoryManagement.Application.Categories;
using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Core.Common;
using InventoryManagement.Domain.Entities;
using InventoryManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Infrastructure.Categories;

public sealed class CategoryService : ICategoryService
{
    private readonly IAppDbContext _context;
    private readonly IAuditLogger _auditLogger;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ICurrentUserSession _session;

    public CategoryService(
        IAppDbContext context,
        IAuditLogger auditLogger,
        IDateTimeProvider dateTimeProvider,
        ICurrentUserSession session)
    {
        _context = context;
        _auditLogger = auditLogger;
        _dateTimeProvider = dateTimeProvider;
        _session = session;
    }

    public async Task<IReadOnlyList<CategorySummary>> GetCategoriesAsync(
        string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Categories.Include(c => c.ParentCategory).AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query = query.Where(c => c.Name.Contains(searchTerm));
        }

        var categories = await query.OrderBy(c => c.Name).ToListAsync(cancellationToken);

        var productCounts = await _context.Products
            .GroupBy(p => p.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count, cancellationToken);

        return categories
            .Select(c => new CategorySummary(
                c.Id, c.Name, c.Description, c.ParentCategoryId, c.ParentCategory?.Name,
                productCounts.GetValueOrDefault(c.Id)))
            .ToList();
    }

    public async Task<Result<CategorySummary>> SaveCategoryAsync(
        SaveCategoryRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<CategorySummary>("Category name is required.");
        }

        Category category;
        AuditAction action;

        if (request.CategoryId is { } existingId)
        {
            var existing = await _context.Categories.SingleOrDefaultAsync(c => c.Id == existingId, cancellationToken);
            if (existing is null)
            {
                return Result.Failure<CategorySummary>("Category not found.");
            }

            category = existing;
            action = AuditAction.Updated;
        }
        else
        {
            var nameTaken = await _context.Categories.AnyAsync(c => c.Name == request.Name, cancellationToken);
            if (nameTaken)
            {
                return Result.Failure<CategorySummary>($"A category named '{request.Name}' already exists.");
            }

            category = new Category();
            _context.Categories.Add(category);
            action = AuditAction.Created;
        }

        if (request.ParentCategoryId == category.Id)
        {
            return Result.Failure<CategorySummary>("A category cannot be its own parent.");
        }

        category.Name = request.Name;
        category.Description = request.Description;
        category.ParentCategoryId = request.ParentCategoryId;

        await _auditLogger.LogAsync(
            action, nameof(Category), category.Id, $"Category '{category.Name}' saved.", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToSummaryAsync(category.Id, cancellationToken));
    }

    public async Task<Result> DeleteCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        var category = await _context.Categories.SingleOrDefaultAsync(c => c.Id == categoryId, cancellationToken);
        if (category is null)
        {
            return Result.Failure("Category not found.");
        }

        var hasProducts = await _context.Products.AnyAsync(p => p.CategoryId == categoryId, cancellationToken);
        if (hasProducts)
        {
            return Result.Failure("Cannot delete a category that still has products assigned to it.");
        }

        var hasChildCategories = await _context.Categories
            .AnyAsync(c => c.ParentCategoryId == categoryId, cancellationToken);
        if (hasChildCategories)
        {
            return Result.Failure("Cannot delete a category that has subcategories. Reassign or delete them first.");
        }

        category.MarkDeleted(_dateTimeProvider.UtcNow, _session.CurrentUser?.Id);

        await _auditLogger.LogAsync(
            AuditAction.Deleted, nameof(Category), category.Id, $"Category '{category.Name}' deleted.", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private async Task<CategorySummary> ToSummaryAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        var category = await _context.Categories
            .Include(c => c.ParentCategory)
            .SingleAsync(c => c.Id == categoryId, cancellationToken);

        var productCount = await _context.Products.CountAsync(p => p.CategoryId == categoryId, cancellationToken);

        return new CategorySummary(
            category.Id, category.Name, category.Description,
            category.ParentCategoryId, category.ParentCategory?.Name, productCount);
    }
}
