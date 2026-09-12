using InventoryManagement.Application.Categories;
using InventoryManagement.Application.Products;
using InventoryManagement.Infrastructure.Auth;
using InventoryManagement.Infrastructure.Categories;
using InventoryManagement.Infrastructure.Data;
using InventoryManagement.Infrastructure.Data.Interceptors;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InventoryManagement.Infrastructure.Tests.Products;

public class CategoryServiceTests : IDisposable
{
    private readonly string _databaseFilePath;
    private readonly InventoryDbContext _context;
    private readonly CategoryService _sut;
    private readonly ProductService _productService;

    public CategoryServiceTests()
    {
        _databaseFilePath = Path.Combine(Path.GetTempPath(), $"InventoryManagementCategoryTests_{Guid.NewGuid()}.db");

        var optionsBuilder = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite($"Data Source={_databaseFilePath}")
            .AddInterceptors(
                new AuditableEntitySaveChangesInterceptor(new SystemDateTimeProvider()),
                new SqliteConnectionInterceptor());

        _context = new InventoryDbContext(optionsBuilder.Options);
        _context.Database.EnsureCreated();

        var session = new CurrentUserSession();
        var auditLogger = new AuditLogger(_context, new SystemDateTimeProvider(), session);

        _sut = new CategoryService(_context, auditLogger, new SystemDateTimeProvider(), session);
        _productService = new ProductService(_context, auditLogger, new SystemDateTimeProvider(), session);
    }

    public void Dispose()
    {
        _context.Dispose();
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databaseFilePath))
        {
            File.Delete(_databaseFilePath);
        }
    }

    [Fact]
    public async Task SaveCategoryAsync_CreatesThenUpdates()
    {
        var created = await _sut.SaveCategoryAsync(new SaveCategoryRequest(null, "Snacks", "Salty snacks", null));
        Assert.True(created.IsSuccess);

        var updated = await _sut.SaveCategoryAsync(
            new SaveCategoryRequest(created.Value.Id, "Snacks", "Updated description", null));

        Assert.True(updated.IsSuccess);
        Assert.Equal("Updated description", updated.Value.Description);
    }

    [Fact]
    public async Task SaveCategoryAsync_WithDuplicateName_Fails()
    {
        await _sut.SaveCategoryAsync(new SaveCategoryRequest(null, "Snacks", null, null));

        var result = await _sut.SaveCategoryAsync(new SaveCategoryRequest(null, "Snacks", null, null));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task DeleteCategoryAsync_WithProductsAssigned_Fails()
    {
        var category = await _sut.SaveCategoryAsync(new SaveCategoryRequest(null, "Beverages", null, null));
        await _productService.CreateProductAsync(new CreateProductRequest(
            "SKU-1", null, "Cola", null, null, category.Value.Id, "pcs", 5m, 10m, 0m, 0m, 0m));

        var result = await _sut.DeleteCategoryAsync(category.Value.Id);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task DeleteCategoryAsync_WithNoProducts_Succeeds()
    {
        var category = await _sut.SaveCategoryAsync(new SaveCategoryRequest(null, "Empty Category", null, null));

        var result = await _sut.DeleteCategoryAsync(category.Value.Id);

        Assert.True(result.IsSuccess);

        var remaining = await _sut.GetCategoriesAsync();
        Assert.DoesNotContain(remaining, c => c.Id == category.Value.Id);
    }

    [Fact]
    public async Task DeleteCategoryAsync_WithChildCategories_Fails()
    {
        var parent = await _sut.SaveCategoryAsync(new SaveCategoryRequest(null, "Parent", null, null));
        await _sut.SaveCategoryAsync(new SaveCategoryRequest(null, "Child", null, parent.Value.Id));

        var result = await _sut.DeleteCategoryAsync(parent.Value.Id);

        Assert.True(result.IsFailure);
    }
}
