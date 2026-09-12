using InventoryManagement.Domain.Entities;
using InventoryManagement.Infrastructure.Common;
using InventoryManagement.Infrastructure.Data;
using InventoryManagement.Infrastructure.Data.Interceptors;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InventoryManagement.Infrastructure.Tests.Data;

public class InventoryDbContextTests : IDisposable
{
    private readonly string _databaseFilePath;
    private readonly AuditableEntitySaveChangesInterceptor _auditInterceptor;

    public InventoryDbContextTests()
    {
        _databaseFilePath = Path.Combine(Path.GetTempPath(), $"InventoryManagementTests_{Guid.NewGuid()}.db");
        _auditInterceptor = new AuditableEntitySaveChangesInterceptor(new SystemDateTimeProvider());
    }

    public void Dispose()
    {
        // Ensure every connection to the file is closed before deleting it.
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databaseFilePath))
        {
            File.Delete(_databaseFilePath);
        }

        foreach (var extra in new[] { "-shm", "-wal" })
        {
            var extraFile = _databaseFilePath + extra;
            if (File.Exists(extraFile))
            {
                File.Delete(extraFile);
            }
        }
    }

    private InventoryDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite($"Data Source={_databaseFilePath}")
            .AddInterceptors(_auditInterceptor, new SqliteConnectionInterceptor());

        return new InventoryDbContext(optionsBuilder.Options);
    }

    [Fact]
    public async Task EnsureCreatedAsync_BuildsTheModelAndConnectsSuccessfully()
    {
        await using var context = CreateContext();

        var created = await context.Database.EnsureCreatedAsync();

        Assert.True(created);
        Assert.True(await context.Database.CanConnectAsync());
    }

    [Fact]
    public async Task SeedData_CreatesExpectedRolesAndPermissions()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        Assert.Equal(19, await context.Permissions.CountAsync());
        Assert.Equal(2, await context.Roles.CountAsync());

        var administrator = await context.Roles
            .Include(r => r.Permissions)
            .SingleAsync(r => r.Name == "Administrator");

        Assert.Equal(19, administrator.Permissions.Count);

        var standardUser = await context.Roles
            .Include(r => r.Permissions)
            .SingleAsync(r => r.Name == "Standard User");

        Assert.Equal(7, standardUser.Permissions.Count);
    }

    [Fact]
    public async Task SavingNewEntity_StampsCreatedAtUtc()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var category = new Category { Name = "Beverages" };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        Assert.NotEqual(default, category.CreatedAtUtc);
    }

    [Fact]
    public async Task SoftDeletedProduct_IsExcludedFromDefaultQueries_ButVisibleWithIgnoreQueryFilters()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var category = new Category { Name = "Snacks" };
        var product = new Product
        {
            Sku = "SKU-0001",
            Name = "Chips",
            Category = category,
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        product.MarkDeleted(DateTimeOffset.UtcNow, byUserId: null);
        await context.SaveChangesAsync();

        var visibleCount = await context.Products.CountAsync(p => p.Id == product.Id);
        var totalCount = await context.Products.IgnoreQueryFilters().CountAsync(p => p.Id == product.Id);

        Assert.Equal(0, visibleCount);
        Assert.Equal(1, totalCount);
    }

    [Fact]
    public async Task DuplicateSku_ViolatesUniqueIndex()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var category = new Category { Name = "Tools" };
        context.Categories.Add(category);
        context.Products.Add(new Product { Sku = "DUP-001", Name = "Hammer", Category = category });
        await context.SaveChangesAsync();

        context.Products.Add(new Product { Sku = "DUP-001", Name = "Wrench", Category = category });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task ForeignKeyConstraint_RejectsProductWithUnknownCategory()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        // CategoryId points at nothing that exists - the SqliteConnectionInterceptor
        // enables PRAGMA foreign_keys=ON, so this must be rejected at save time
        // rather than silently succeeding (SQLite's default behavior without it).
        context.Products.Add(new Product
        {
            Sku = "ORPHAN-001",
            Name = "Orphaned Product",
            CategoryId = Guid.NewGuid(),
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
