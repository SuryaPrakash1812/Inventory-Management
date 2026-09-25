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

public class InventoryServiceTests : IDisposable
{
    private readonly string _databaseFilePath;
    private readonly InventoryDbContext _context;
    private readonly InventoryService _sut;
    private readonly Category _category;
    private readonly Product _product;

    public InventoryServiceTests()
    {
        _databaseFilePath = Path.Combine(Path.GetTempPath(), $"InventoryManagementInventoryTests_{Guid.NewGuid()}.db");

        _context = CreateContext(_databaseFilePath);
        _context.Database.EnsureCreated();

        _category = new Category { Name = "Beverages" };
        _product = new Product
        {
            Sku = "SKU-1",
            Name = "Cola",
            Category = _category,
            ReorderLevel = 10,
            CostPrice = 5m,
            SellingPrice = 10m,
            IsActive = true,
        };
        _context.Products.Add(_product);
        _context.SaveChanges();

        _sut = new InventoryService(_context, new SystemDateTimeProvider(), new CurrentUserSession());
    }

    public void Dispose()
    {
        _context.Dispose();
        SqliteConnection.ClearAllPools();

        foreach (var extra in new[] { string.Empty, "-shm", "-wal" })
        {
            var file = _databaseFilePath + extra;
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    private static InventoryDbContext CreateContext(string databaseFilePath)
    {
        var optionsBuilder = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite($"Data Source={databaseFilePath}")
            .AddInterceptors(
                new AuditableEntitySaveChangesInterceptor(new SystemDateTimeProvider()),
                new SqliteConnectionInterceptor());

        return new InventoryDbContext(optionsBuilder.Options);
    }

    [Fact]
    public async Task GetCurrentStockAsync_ForNewProduct_IsZero()
    {
        var stock = await _sut.GetCurrentStockAsync(_product.Id);

        Assert.Equal(0, stock);
    }

    [Fact]
    public async Task AddStockAsync_WithPurchaseReceipt_IncreasesStock()
    {
        var result = await _sut.AddStockAsync(new AddStockRequest(
            _product.Id, 50m, StockMovementType.PurchaseReceipt, StockReferenceType.Purchase, Guid.NewGuid()));

        Assert.True(result.IsSuccess);
        Assert.Equal(50m, result.Value.QuantityBalanceAfter);
        Assert.Equal(50m, await _sut.GetCurrentStockAsync(_product.Id));
    }

    [Fact]
    public async Task RemoveStockAsync_WithSaleIssue_DecreasesStock()
    {
        await _sut.AddStockAsync(new AddStockRequest(
            _product.Id, 50m, StockMovementType.PurchaseReceipt, StockReferenceType.Purchase, Guid.NewGuid()));

        var result = await _sut.RemoveStockAsync(new RemoveStockRequest(
            _product.Id, 20m, StockMovementType.SaleIssue, StockReferenceType.Sale, Guid.NewGuid()));

        Assert.True(result.IsSuccess);
        Assert.Equal(30m, result.Value.QuantityBalanceAfter);
        Assert.Equal(30m, await _sut.GetCurrentStockAsync(_product.Id));
    }

    [Fact]
    public async Task AddStockAsync_WithSalesReturn_IncreasesStock()
    {
        await _sut.AddStockAsync(new AddStockRequest(
            _product.Id, 50m, StockMovementType.PurchaseReceipt, StockReferenceType.Purchase, Guid.NewGuid()));
        await _sut.RemoveStockAsync(new RemoveStockRequest(
            _product.Id, 10m, StockMovementType.SaleIssue, StockReferenceType.Sale, Guid.NewGuid()));

        var result = await _sut.AddStockAsync(new AddStockRequest(
            _product.Id, 3m, StockMovementType.SalesReturn, StockReferenceType.Sale, Guid.NewGuid()));

        Assert.True(result.IsSuccess);
        Assert.Equal(43m, result.Value.QuantityBalanceAfter);
    }

    [Fact]
    public async Task RemoveStockAsync_WithPurchaseReturn_DecreasesStock()
    {
        await _sut.AddStockAsync(new AddStockRequest(
            _product.Id, 50m, StockMovementType.PurchaseReceipt, StockReferenceType.Purchase, Guid.NewGuid()));

        var result = await _sut.RemoveStockAsync(new RemoveStockRequest(
            _product.Id, 5m, StockMovementType.PurchaseReturn, StockReferenceType.Purchase, Guid.NewGuid()));

        Assert.True(result.IsSuccess);
        Assert.Equal(45m, result.Value.QuantityBalanceAfter);
    }

    [Fact]
    public async Task AdjustStockAsync_WithPositiveChange_RecordsAdjustmentIncrease()
    {
        var result = await _sut.AdjustStockAsync(new AdjustStockRequest(
            _product.Id, 7m, StockReferenceType.StockAdjustment, Guid.NewGuid()));

        Assert.True(result.IsSuccess);
        Assert.Equal(7m, result.Value.QuantityBalanceAfter);

        var movement = await _context.StockMovements.SingleAsync(m => m.Id == result.Value.MovementId);
        Assert.Equal(StockMovementType.AdjustmentIncrease, movement.MovementType);
    }

    [Fact]
    public async Task AdjustStockAsync_WithNegativeChange_RecordsAdjustmentDecrease()
    {
        await _sut.AddStockAsync(new AddStockRequest(
            _product.Id, 50m, StockMovementType.PurchaseReceipt, StockReferenceType.Purchase, Guid.NewGuid()));

        var result = await _sut.AdjustStockAsync(new AdjustStockRequest(
            _product.Id, -6m, StockReferenceType.StockAdjustment, Guid.NewGuid(), Notes: "Damaged stock found during count"));

        Assert.True(result.IsSuccess);
        Assert.Equal(44m, result.Value.QuantityBalanceAfter);

        var movement = await _context.StockMovements.SingleAsync(m => m.Id == result.Value.MovementId);
        Assert.Equal(StockMovementType.AdjustmentDecrease, movement.MovementType);
    }

    [Fact]
    public async Task AdjustStockAsync_WithZeroChange_Fails()
    {
        var result = await _sut.AdjustStockAsync(new AdjustStockRequest(
            _product.Id, 0m, StockReferenceType.StockAdjustment, Guid.NewGuid()));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task RemoveStockAsync_WithInsufficientStock_FailsAndChangesNothing()
    {
        // Product starts at 0 stock.
        var result = await _sut.RemoveStockAsync(new RemoveStockRequest(
            _product.Id, 10m, StockMovementType.SaleIssue, StockReferenceType.Sale, Guid.NewGuid()));

        Assert.True(result.IsFailure);

        // Rollback must have undone the write-lock-forcing update too - the
        // balance is unchanged and no movement row exists at all.
        Assert.Equal(0, await _sut.GetCurrentStockAsync(_product.Id));
        Assert.Empty(await _context.StockMovements.Where(m => m.ProductId == _product.Id).ToListAsync());
    }

    [Fact]
    public async Task RemoveStockAsync_WithInsufficientStock_ButAllowNegativeStock_Succeeds()
    {
        var result = await _sut.RemoveStockAsync(new RemoveStockRequest(
            _product.Id, 10m, StockMovementType.SaleIssue, StockReferenceType.Sale, Guid.NewGuid(),
            AllowNegativeStock: true));

        Assert.True(result.IsSuccess);
        Assert.Equal(-10m, result.Value.QuantityBalanceAfter);
    }

    [Fact]
    public async Task AddStockAsync_WithADecreaseMovementType_Fails()
    {
        var result = await _sut.AddStockAsync(new AddStockRequest(
            _product.Id, 10m, StockMovementType.SaleIssue, StockReferenceType.Sale, Guid.NewGuid()));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task RemoveStockAsync_WithAnIncreaseMovementType_Fails()
    {
        var result = await _sut.RemoveStockAsync(new RemoveStockRequest(
            _product.Id, 10m, StockMovementType.PurchaseReceipt, StockReferenceType.Purchase, Guid.NewGuid()));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task AddStockAsync_WithNonPositiveQuantity_Fails()
    {
        var result = await _sut.AddStockAsync(new AddStockRequest(
            _product.Id, 0m, StockMovementType.PurchaseReceipt, StockReferenceType.Purchase, Guid.NewGuid()));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task DecimalQuantities_AreSupportedForFractionalUnits()
    {
        // e.g. selling by the kilogram.
        var result = await _sut.AddStockAsync(new AddStockRequest(
            _product.Id, 12.5m, StockMovementType.PurchaseReceipt, StockReferenceType.Purchase, Guid.NewGuid()));

        Assert.True(result.IsSuccess);

        var removed = await _sut.RemoveStockAsync(new RemoveStockRequest(
            _product.Id, 2.25m, StockMovementType.SaleIssue, StockReferenceType.Sale, Guid.NewGuid()));

        Assert.True(removed.IsSuccess);
        Assert.Equal(10.25m, removed.Value.QuantityBalanceAfter);
    }

    [Fact]
    public async Task GetStockHistoryAsync_ReturnsMovementsNewestFirstWithPagination()
    {
        for (var i = 1; i <= 5; i++)
        {
            await _sut.AddStockAsync(new AddStockRequest(
                _product.Id, i, StockMovementType.PurchaseReceipt, StockReferenceType.Purchase, Guid.NewGuid()));
        }

        var page1 = await _sut.GetStockHistoryAsync(new StockHistoryQuery
        {
            ProductId = _product.Id, PageNumber = 1, PageSize = 3,
        });

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(3, page1.Items.Count);
        Assert.Equal(_product.Sku, page1.Items[0].ProductSku);
    }

    [Fact]
    public async Task GetLowStockProductsAsync_ReturnsProductsAtOrBelowReorderLevel()
    {
        // _product has ReorderLevel = 10.
        await _sut.AddStockAsync(new AddStockRequest(
            _product.Id, 5m, StockMovementType.PurchaseReceipt, StockReferenceType.Purchase, Guid.NewGuid()));

        var wellStocked = new Product
        {
            Sku = "SKU-2", Name = "Chips", Category = _category, ReorderLevel = 5, IsActive = true,
        };
        _context.Products.Add(wellStocked);
        await _context.SaveChangesAsync();
        await _sut.AddStockAsync(new AddStockRequest(
            wellStocked.Id, 100m, StockMovementType.PurchaseReceipt, StockReferenceType.Purchase, Guid.NewGuid()));

        var lowStock = await _sut.GetLowStockProductsAsync();

        Assert.Single(lowStock);
        Assert.Equal(_product.Id, lowStock[0].ProductId);
    }

    [Fact]
    public async Task GetStockValuationAsync_ComputesCostAndRetailTotals()
    {
        // _product: CostPrice 5, SellingPrice 10.
        await _sut.AddStockAsync(new AddStockRequest(
            _product.Id, 20m, StockMovementType.PurchaseReceipt, StockReferenceType.Purchase, Guid.NewGuid()));

        var valuation = await _sut.GetStockValuationAsync();

        Assert.Equal(1, valuation.ProductCount);
        Assert.Equal(100m, valuation.TotalCostValue); // 20 * 5
        Assert.Equal(200m, valuation.TotalRetailValue); // 20 * 10
    }

    [Fact]
    public async Task ConcurrentAddStockCalls_BothSucceedWithNoLostUpdate()
    {
        // Two separate DbContext/InventoryService instances against the same
        // database file, simulating two different concurrent callers (a
        // single DbContext is never safe to use from multiple threads at
        // once, so this - not sharing one context - is the realistic test
        // of the actual concurrency guarantee).
        await using var contextA = CreateContext(_databaseFilePath);
        await using var contextB = CreateContext(_databaseFilePath);

        var serviceA = new InventoryService(contextA, new SystemDateTimeProvider(), new CurrentUserSession());
        var serviceB = new InventoryService(contextB, new SystemDateTimeProvider(), new CurrentUserSession());

        var taskA = serviceA.AddStockAsync(new AddStockRequest(
            _product.Id, 10m, StockMovementType.PurchaseReceipt, StockReferenceType.Purchase, Guid.NewGuid()));
        var taskB = serviceB.AddStockAsync(new AddStockRequest(
            _product.Id, 15m, StockMovementType.PurchaseReceipt, StockReferenceType.Purchase, Guid.NewGuid()));

        var results = await Task.WhenAll(taskA, taskB);

        Assert.All(results, r => Assert.True(r.IsSuccess));

        var finalStock = await _sut.GetCurrentStockAsync(_product.Id);
        Assert.Equal(25m, finalStock);

        var movementCount = await _context.StockMovements.CountAsync(m => m.ProductId == _product.Id);
        Assert.Equal(2, movementCount);
    }
}
