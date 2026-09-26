using InventoryManagement.Application.Inventory;
using InventoryManagement.Application.Sales;
using InventoryManagement.Domain.Entities;
using InventoryManagement.Domain.Enums;
using InventoryManagement.Infrastructure.Auth;
using InventoryManagement.Infrastructure.Common;
using InventoryManagement.Infrastructure.Data;
using InventoryManagement.Infrastructure.Data.Interceptors;
using InventoryManagement.Infrastructure.Inventory;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InventoryManagement.Infrastructure.Tests.Sales;

public sealed class SaleServiceTests : IDisposable
{
    private readonly string _databaseFilePath;
    private readonly InventoryDbContext _context;
    private readonly InventoryService _inventoryService;
    private readonly Infrastructure.Sales.SaleService _sut;
    private readonly Customer _customer;
    private readonly Category _category;
    private readonly Product _product;

    public SaleServiceTests()
    {
        _databaseFilePath = Path.Combine(Path.GetTempPath(), $"InventoryManagementSaleTests_{Guid.NewGuid()}.db");

        var optionsBuilder = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite($"Data Source={_databaseFilePath}")
            .AddInterceptors(
                new AuditableEntitySaveChangesInterceptor(new SystemDateTimeProvider()),
                new SqliteConnectionInterceptor());

        _context = new InventoryDbContext(optionsBuilder.Options);
        _context.Database.EnsureCreated();

        _customer = new Customer { Name = "Acme Retail" };
        _category = new Category { Name = "Beverages" };
        _product = new Product
        {
            Sku = "SKU-1", Name = "Cola", Category = _category, CostPrice = 5m, SellingPrice = 10m,
            QuantityOnHand = 50m,
        };
        _context.Customers.Add(_customer);
        _context.Categories.Add(_category);
        _context.Products.Add(_product);
        _context.SaveChanges();

        var session = new CurrentUserSession();
        _inventoryService = new InventoryService(_context, new SystemDateTimeProvider(), session);
        _sut = new Infrastructure.Sales.SaleService(
            _context, _inventoryService, new AuditLogger(_context, new SystemDateTimeProvider(), session), new SystemDateTimeProvider());
    }

    private SaveDraftSaleRequest MakeDraftRequest(Guid? saleId = null, decimal quantity = 5, decimal unitPrice = 10) =>
        new(
            saleId, _customer.Id, DateTimeOffset.UtcNow, "Test sale",
            new[] { new SaleItemRequest(_product.Id, quantity, unitPrice, DiscountAmount: 0) });

    [Fact]
    public async Task SaveDraftAsync_CreatesNewDraft_WithNoStockChangeYet_AndNoInvoiceNumber()
    {
        var result = await _sut.SaveDraftAsync(MakeDraftRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(SaleStatus.Draft, result.Value.Status);
        Assert.StartsWith("SO-", result.Value.SaleNumber);
        Assert.Null(result.Value.InvoiceNumber);

        var stock = await _inventoryService.GetCurrentStockAsync(_product.Id);
        Assert.Equal(50m, stock);
    }

    [Fact]
    public async Task SaveDraftAsync_ComputesLineTotalsWithoutTax()
    {
        // 3 units at 20 each = 60 subtotal, minus 5 discount = 55 line total.
        // No tax term at all - see SaleWorkflow's remarks.
        var request = new SaveDraftSaleRequest(
            null, _customer.Id, DateTimeOffset.UtcNow, null,
            new[] { new SaleItemRequest(_product.Id, 3, 20, 5) });

        var result = await _sut.SaveDraftAsync(request);

        Assert.True(result.IsSuccess);
        var item = result.Value.Items.Single();
        Assert.Equal(55m, item.LineTotal);
        Assert.Equal(0m, result.Value.TaxAmount);
        Assert.Equal(55m, result.Value.TotalAmount);
    }

    [Fact]
    public async Task SaveDraftAsync_WithNoItems_Fails()
    {
        var request = new SaveDraftSaleRequest(null, _customer.Id, DateTimeOffset.UtcNow, null, Array.Empty<SaleItemRequest>());

        var result = await _sut.SaveDraftAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal("At least one item is required.", result.Error);
    }

    [Fact]
    public async Task SaveDraftAsync_WithNonexistentCustomer_Fails()
    {
        var request = new SaveDraftSaleRequest(
            null, Guid.NewGuid(), DateTimeOffset.UtcNow, null,
            new[] { new SaleItemRequest(_product.Id, 1, 10, 0) });

        var result = await _sut.SaveDraftAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal("Selected customer does not exist.", result.Error);
    }

    [Fact]
    public async Task SaveDraftAsync_WithZeroQuantity_Fails()
    {
        var result = await _sut.SaveDraftAsync(MakeDraftRequest(quantity: 0));

        Assert.True(result.IsFailure);
        Assert.Equal("Item quantity must be greater than zero.", result.Error);
    }

    [Fact]
    public async Task SaveDraftAsync_ValidationFailure_CreatesNothing()
    {
        var countBefore = await _context.Sales.CountAsync();

        await _sut.SaveDraftAsync(MakeDraftRequest(quantity: 0));

        Assert.Equal(countBefore, await _context.Sales.CountAsync());
    }

    [Fact]
    public async Task InvoiceAsync_ReducesStock_CreatesSaleIssueMovement_AndAssignsInvoiceNumber()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest(quantity: 15));

        var invoiced = await _sut.InvoiceAsync(created.Value.Id);

        Assert.True(invoiced.IsSuccess);
        Assert.Equal(SaleStatus.Invoiced, invoiced.Value.Status);
        Assert.StartsWith("INV-", invoiced.Value.InvoiceNumber);

        var stock = await _inventoryService.GetCurrentStockAsync(_product.Id);
        Assert.Equal(35m, stock); // 50 - 15

        var movement = await _context.StockMovements.SingleAsync(m => m.ReferenceId == created.Value.Id);
        Assert.Equal(StockMovementType.SaleIssue, movement.MovementType);
        Assert.Equal(-15m, movement.QuantityChange);
    }

    [Fact]
    public async Task InvoiceAsync_InsufficientStock_FailsAndChangesNothing()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest(quantity: 100)); // only 50 in stock

        var invoiced = await _sut.InvoiceAsync(created.Value.Id);

        Assert.True(invoiced.IsFailure);
        Assert.Contains("Insufficient stock", invoiced.Error);

        var stock = await _inventoryService.GetCurrentStockAsync(_product.Id);
        Assert.Equal(50m, stock); // unchanged

        Assert.Empty(await _context.StockMovements.Where(m => m.ReferenceId == created.Value.Id).ToListAsync());

        var reloaded = await _sut.GetSaleByIdAsync(created.Value.Id);
        Assert.Equal(SaleStatus.Draft, reloaded!.Status); // never transitioned
        Assert.Null(reloaded.InvoiceNumber); // never assigned
    }

    [Fact]
    public async Task InvoiceAsync_CalledTwice_IsIdempotent_NeverIssuesStockTwice()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest(quantity: 10));
        await _sut.InvoiceAsync(created.Value.Id);

        var secondInvoice = await _sut.InvoiceAsync(created.Value.Id);

        Assert.True(secondInvoice.IsFailure);
        Assert.Equal("Sale is already Invoiced and cannot be invoiced again.", secondInvoice.Error);

        var stock = await _inventoryService.GetCurrentStockAsync(_product.Id);
        Assert.Equal(40m, stock); // only issued once
    }

    [Fact]
    public async Task CancelAsync_FromDraft_Succeeds_WithNoStockMovements()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest());

        var cancelled = await _sut.CancelAsync(created.Value.Id);

        Assert.True(cancelled.IsSuccess);
        Assert.Equal(SaleStatus.Cancelled, cancelled.Value.Status);
        Assert.Empty(await _context.StockMovements.Where(m => m.ReferenceId == created.Value.Id).ToListAsync());
    }

    [Fact]
    public async Task CancelAsync_FromInvoiced_RestoresStock_WithSalesReturnMovement()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest(quantity: 15));
        await _sut.InvoiceAsync(created.Value.Id); // stock now 35

        var cancelled = await _sut.CancelAsync(created.Value.Id);

        Assert.True(cancelled.IsSuccess);

        var stock = await _inventoryService.GetCurrentStockAsync(_product.Id);
        Assert.Equal(50m, stock); // fully restored

        var returnMovement = await _context.StockMovements
            .Where(m => m.ReferenceId == created.Value.Id && m.MovementType == StockMovementType.SalesReturn)
            .SingleAsync();
        Assert.Equal(15m, returnMovement.QuantityChange);
    }

    [Fact]
    public async Task CancelAsync_CalledTwice_IsIdempotent()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest());
        await _sut.CancelAsync(created.Value.Id);

        var secondCancel = await _sut.CancelAsync(created.Value.Id);

        Assert.True(secondCancel.IsFailure);
        Assert.Equal("Sale is already cancelled.", secondCancel.Error);
    }

    [Fact]
    public async Task DeleteDraftAsync_OnInvoicedSale_Fails()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest());
        await _sut.InvoiceAsync(created.Value.Id);

        var result = await _sut.DeleteDraftAsync(created.Value.Id);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task SaleNumbers_AreUniqueAcrossMultipleDrafts()
    {
        var first = await _sut.SaveDraftAsync(MakeDraftRequest());
        var second = await _sut.SaveDraftAsync(MakeDraftRequest());

        Assert.NotEqual(first.Value.SaleNumber, second.Value.SaleNumber);
    }

    [Fact]
    public async Task InvoiceNumbers_AreUniqueAcrossMultipleSales()
    {
        var first = await _sut.SaveDraftAsync(MakeDraftRequest());
        var second = await _sut.SaveDraftAsync(MakeDraftRequest());

        var firstInvoiced = await _sut.InvoiceAsync(first.Value.Id);
        var secondInvoiced = await _sut.InvoiceAsync(second.Value.Id);

        Assert.NotEqual(firstInvoiced.Value.InvoiceNumber, secondInvoiced.Value.InvoiceNumber);
    }

    public void Dispose()
    {
        _context.Dispose();
        if (File.Exists(_databaseFilePath))
        {
            File.Delete(_databaseFilePath);
        }
    }
}
