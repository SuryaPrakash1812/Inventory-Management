using InventoryManagement.Application.Inventory;
using InventoryManagement.Domain.Entities;
using InventoryManagement.Domain.Enums;
using InventoryManagement.Infrastructure.Auth;
using InventoryManagement.Infrastructure.Common;
using InventoryManagement.Infrastructure.Data;
using InventoryManagement.Infrastructure.Data.Interceptors;
using InventoryManagement.Infrastructure.Inventory;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InventoryManagement.Infrastructure.Tests.Inventory;

/// <summary>
/// The Reports page used to fetch one page of movements and THEN filter it by
/// date, so movements outside the first page were silently missing. The
/// filter now runs in the service, before paging.
/// </summary>
public class StockHistoryDateFilterTests : IDisposable
{
    private readonly string _databaseFilePath = Path.Combine(Path.GetTempPath(), $"InventoryManagementHistoryTests_{Guid.NewGuid()}.db");
    private readonly InventoryDbContext _context;
    private readonly InventoryService _sut;
    private readonly Product _product;

    public StockHistoryDateFilterTests()
    {
        _context = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite($"Data Source={_databaseFilePath}")
            .AddInterceptors(new AuditableEntitySaveChangesInterceptor(new SystemDateTimeProvider()), new SqliteConnectionInterceptor())
            .Options);
        _context.Database.EnsureCreated();

        _product = new Product { Sku = "SKU-1", Name = "Cola", Category = new Category { Name = "Drinks" }, IsActive = true };
        _context.Products.Add(_product);
        _context.SaveChanges();

        _sut = new InventoryService(_context, new SystemDateTimeProvider(), new CurrentUserSession());
    }

    public void Dispose()
    {
        _context.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-shm", "-wal" })
        {
            if (File.Exists(_databaseFilePath + suffix))
            {
                File.Delete(_databaseFilePath + suffix);
            }
        }
    }

    [Fact]
    public async Task DateRange_IsAppliedBeforePaging_AndTotalCountReflectsTheFilter()
    {
        // Ten movements, one per day, 1st-10th October.
        for (var day = 1; day <= 10; day++)
        {
            await _sut.AddStockAsync(new AddStockRequest(
                _product.Id, 1m, StockMovementType.PurchaseReceipt, StockReferenceType.Purchase, Guid.NewGuid()));
        }

        var movements = await _context.StockMovements.ToListAsync();
        for (var i = 0; i < movements.Count; i++)
        {
            movements[i].OccurredAtUtc = new DateTimeOffset(2026, 10, i + 1, 12, 0, 0, TimeSpan.Zero);
        }

        await _context.SaveChangesAsync();

        var result = await _sut.GetStockHistoryAsync(new StockHistoryQuery
        {
            FromDate = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero),
            ToDate = new DateTimeOffset(2026, 10, 4, 23, 59, 59, TimeSpan.Zero),
            PageSize = 2,
        });

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(4, result.Items[0].OccurredAtUtc.Day); // newest first
        Assert.All(result.Items, m => Assert.InRange(m.OccurredAtUtc.Day, 2, 4));
    }
}
