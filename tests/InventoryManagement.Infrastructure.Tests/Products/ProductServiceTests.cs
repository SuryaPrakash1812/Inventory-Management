using InventoryManagement.Application.Products;
using InventoryManagement.Domain.Entities;
using InventoryManagement.Infrastructure.Auth;
using InventoryManagement.Infrastructure.Data;
using InventoryManagement.Infrastructure.Data.Interceptors;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InventoryManagement.Infrastructure.Tests.Products;

public class ProductServiceTests : IDisposable
{
    private readonly string _databaseFilePath;
    private readonly InventoryDbContext _context;
    private readonly ProductService _sut;
    private readonly Category _category;

    public ProductServiceTests()
    {
        _databaseFilePath = Path.Combine(Path.GetTempPath(), $"InventoryManagementProductTests_{Guid.NewGuid()}.db");

        var optionsBuilder = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite($"Data Source={_databaseFilePath}")
            .AddInterceptors(
                new AuditableEntitySaveChangesInterceptor(new SystemDateTimeProvider()),
                new SqliteConnectionInterceptor());

        _context = new InventoryDbContext(optionsBuilder.Options);
        _context.Database.EnsureCreated();

        _category = new Category { Name = "Beverages" };
        _context.Categories.Add(_category);
        _context.SaveChanges();

        var session = new CurrentUserSession();
        _sut = new ProductService(
            _context,
            new AuditLogger(_context, new SystemDateTimeProvider(), session),
            new SystemDateTimeProvider(),
            session);
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

    private CreateProductRequest MakeRequest(string sku, string name, decimal sellingPrice = 10m, decimal initialStock = 0) =>
        new(sku, null, name, null, null, _category.Id, "pcs", 5m, sellingPrice, 0m, 0m, initialStock);

    [Fact]
    public async Task CreateProductAsync_ThenGetById_RoundTrips()
    {
        var created = await _sut.CreateProductAsync(MakeRequest("SKU-001", "Cola"));

        Assert.True(created.IsSuccess);

        var detail = await _sut.GetProductByIdAsync(created.Value.Id);

        Assert.NotNull(detail);
        Assert.Equal("SKU-001", detail!.Sku);
        Assert.Equal("Cola", detail.Name);
    }

    [Fact]
    public async Task CreateProductAsync_WithDuplicateSku_Fails()
    {
        await _sut.CreateProductAsync(MakeRequest("SKU-002", "Cola"));

        var result = await _sut.CreateProductAsync(MakeRequest("SKU-002", "Different Name"));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task CreateProductAsync_WithInitialStock_WritesOpeningBalanceMovement()
    {
        var created = await _sut.CreateProductAsync(MakeRequest("SKU-003", "Cola", initialStock: 50));

        Assert.True(created.IsSuccess);
        Assert.Equal(50, created.Value.CurrentStock);

        var movement = await _context.StockMovements.SingleAsync(m => m.ProductId == created.Value.Id);
        Assert.Equal(Domain.Enums.StockMovementType.OpeningBalance, movement.MovementType);
        Assert.Equal(50, movement.QuantityChange);
    }

    [Fact]
    public async Task GetProductsAsync_SearchMatchesSkuBarcodeAndName()
    {
        await _sut.CreateProductAsync(MakeRequest("ABC-100", "Widget"));
        await _sut.CreateProductAsync(new CreateProductRequest(
            "XYZ-200", "999888777", "Gadget", null, null, _category.Id, "pcs", 5m, 10m, 0m, 0m, 0m));
        await _sut.CreateProductAsync(MakeRequest("QRS-300", "Unrelated Thing"));

        var bySku = await _sut.GetProductsAsync(new ProductQueryParameters { SearchTerm = "ABC" });
        var byBarcode = await _sut.GetProductsAsync(new ProductQueryParameters { SearchTerm = "999888" });
        var byName = await _sut.GetProductsAsync(new ProductQueryParameters { SearchTerm = "Widget" });

        Assert.Single(bySku.Items);
        Assert.Equal("ABC-100", bySku.Items[0].Sku);
        Assert.Single(byBarcode.Items);
        Assert.Equal("XYZ-200", byBarcode.Items[0].Sku);
        Assert.Single(byName.Items);
    }

    [Fact]
    public async Task GetProductsAsync_Pagination_ReturnsCorrectPageAndTotalCount()
    {
        for (var i = 1; i <= 30; i++)
        {
            await _sut.CreateProductAsync(MakeRequest($"P-{i:D3}", $"Product {i:D3}"));
        }

        var page1 = await _sut.GetProductsAsync(new ProductQueryParameters { PageNumber = 1, PageSize = 10 });
        var page2 = await _sut.GetProductsAsync(new ProductQueryParameters { PageNumber = 2, PageSize = 10 });
        var page4 = await _sut.GetProductsAsync(new ProductQueryParameters { PageNumber = 4, PageSize = 10 });

        Assert.Equal(30, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages);
        Assert.Equal(10, page1.Items.Count);
        Assert.Equal(10, page2.Items.Count);
        Assert.Empty(page4.Items);
        Assert.NotEqual(page1.Items[0].Sku, page2.Items[0].Sku);
    }

    [Fact]
    public async Task GetProductsAsync_SortBySellingPriceDescending_OrdersCorrectly()
    {
        await _sut.CreateProductAsync(MakeRequest("A", "A", sellingPrice: 5));
        await _sut.CreateProductAsync(MakeRequest("B", "B", sellingPrice: 20));
        await _sut.CreateProductAsync(MakeRequest("C", "C", sellingPrice: 10));

        var result = await _sut.GetProductsAsync(new ProductQueryParameters
        {
            SortColumn = ProductSortColumn.SellingPrice,
            SortDescending = true,
        });

        Assert.Equal(new[] { 20m, 10m, 5m }, result.Items.Select(p => p.SellingPrice));
    }

    [Fact]
    public async Task GetProductsAsync_FiltersByActiveStatus()
    {
        var created = await _sut.CreateProductAsync(MakeRequest("ACT-1", "Active One"));
        await _sut.CreateProductAsync(MakeRequest("ACT-2", "Active Two"));
        await _sut.DeactivateProductAsync(created.Value.Id);

        var activeOnly = await _sut.GetProductsAsync(new ProductQueryParameters { IsActive = true });
        var inactiveOnly = await _sut.GetProductsAsync(new ProductQueryParameters { IsActive = false });

        Assert.Single(activeOnly.Items);
        Assert.Single(inactiveOnly.Items);
    }

    [Fact]
    public async Task ExportThenImport_RoundTripsExistingProductAsUpdate()
    {
        var created = await _sut.CreateProductAsync(MakeRequest("EXP-1", "Exportable"));
        var csv = await _sut.ExportToCsvAsync(new ProductQueryParameters());

        // Modify the exported name to simulate an edit made in a spreadsheet.
        var modifiedCsv = csv.Replace("Exportable", "Exportable Renamed");

        var importResult = await _sut.ImportFromCsvAsync(modifiedCsv);

        Assert.Equal(0, importResult.CreatedCount);
        Assert.Equal(1, importResult.UpdatedCount);
        Assert.Empty(importResult.Errors);

        var updated = await _sut.GetProductByIdAsync(created.Value.Id);
        Assert.Equal("Exportable Renamed", updated!.Name);
    }

    [Fact]
    public async Task ImportFromCsvAsync_WithUnknownCategory_RecordsRowError()
    {
        const string csv = "Sku,Name,Category,PurchasePrice,SellingPrice\nNEW-1,New Product,NoSuchCategory,5,10\n";

        var result = await _sut.ImportFromCsvAsync(csv);

        Assert.Equal(0, result.CreatedCount);
        Assert.Single(result.Errors);
    }

    [Fact]
    public async Task ImportFromCsvAsync_NeverOverwritesStockForExistingProduct()
    {
        var created = await _sut.CreateProductAsync(MakeRequest("STK-1", "Stocked", initialStock: 100));

        var csv = $"Sku,Name,Category,PurchasePrice,SellingPrice,CurrentStock\nSTK-1,Stocked,Beverages,5,10,999999\n";
        await _sut.ImportFromCsvAsync(csv);

        var reloaded = await _sut.GetProductByIdAsync(created.Value.Id);
        Assert.Equal(100, reloaded!.CurrentStock);
    }
}
