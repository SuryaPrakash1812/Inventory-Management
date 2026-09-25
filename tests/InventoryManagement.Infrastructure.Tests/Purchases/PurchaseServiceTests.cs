using System.Text.Json;
using InventoryManagement.Application.Inventory;
using InventoryManagement.Application.Purchases;
using InventoryManagement.Contracts.Purchases;
using InventoryManagement.Domain.Entities;
using InventoryManagement.Domain.Enums;
using InventoryManagement.Infrastructure.Auth;
using InventoryManagement.Infrastructure.Common;
using InventoryManagement.Infrastructure.Data;
using InventoryManagement.Infrastructure.Data.Interceptors;
using InventoryManagement.Infrastructure.Inventory;
using InventoryManagement.Infrastructure.Purchases;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InventoryManagement.Infrastructure.Tests.Purchases;

public class PurchaseServiceTests : IDisposable
{
    private readonly string _databaseFilePath;
    private readonly InventoryDbContext _context;
    private readonly InventoryService _inventoryService;
    private readonly PurchaseService _sut;
    private readonly Category _category;
    private readonly Product _product;
    private readonly Supplier _supplier;

    public PurchaseServiceTests()
    {
        _databaseFilePath = Path.Combine(Path.GetTempPath(), $"InventoryManagementPurchaseTests_{Guid.NewGuid()}.db");

        var optionsBuilder = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite($"Data Source={_databaseFilePath}")
            .AddInterceptors(
                new AuditableEntitySaveChangesInterceptor(new SystemDateTimeProvider()),
                new SqliteConnectionInterceptor());

        _context = new InventoryDbContext(optionsBuilder.Options);
        _context.Database.EnsureCreated();

        _category = new Category { Name = "Beverages" };
        _product = new Product
        {
            Sku = "SKU-1", Name = "Cola", Category = _category, CostPrice = 5m, SellingPrice = 10m,
        };
        _supplier = new Supplier { Name = "Acme Supplies" };
        _context.Categories.Add(_category);
        _context.Products.Add(_product);
        _context.Suppliers.Add(_supplier);
        _context.SaveChanges();

        var session = new CurrentUserSession();
        var dateTimeProvider = new SystemDateTimeProvider();
        var auditLogger = new AuditLogger(_context, dateTimeProvider, session);

        _inventoryService = new InventoryService(_context, dateTimeProvider, session);
        _sut = new PurchaseService(_context, _inventoryService, auditLogger, dateTimeProvider);
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

    private SaveDraftPurchaseRequest MakeDraftRequest(Guid? purchaseId = null, decimal quantity = 10, decimal unitCost = 5) =>
        new(
            purchaseId,
            _supplier.Id,
            SupplierInvoiceNumber: "INV-001",
            PurchaseDate: DateTimeOffset.UtcNow,
            Notes: "Test purchase",
            Items: new[] { new PurchaseItemRequest(_product.Id, quantity, unitCost, DiscountAmount: 0, TaxPercentage: 10) });

    [Fact]
    public async Task SaveDraftAsync_CreatesNewDraftWithComputedTotals()
    {
        var result = await _sut.SaveDraftAsync(MakeDraftRequest(quantity: 10, unitCost: 5));

        Assert.True(result.IsSuccess);
        Assert.Equal(PurchaseStatus.Draft, result.Value.Status);
        Assert.StartsWith("PO-", result.Value.PurchaseNumber);
        Assert.Equal(50m, result.Value.Subtotal); // 10 * 5
        Assert.Equal(5m, result.Value.TaxAmount); // 50 * 10%
        Assert.Equal(55m, result.Value.TotalAmount);
        Assert.Single(result.Value.Items);
    }

    [Fact]
    public async Task SaveDraftAsync_OnExistingDraft_ReplacesItems()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest(quantity: 10, unitCost: 5));

        var updated = await _sut.SaveDraftAsync(MakeDraftRequest(created.Value.Id, quantity: 20, unitCost: 6));

        Assert.True(updated.IsSuccess);
        Assert.Single(updated.Value.Items);
        Assert.Equal(20m, updated.Value.Items[0].Quantity);
        Assert.Equal(120m, updated.Value.Subtotal); // 20 * 6
    }

    [Fact]
    public async Task SaveDraftAsync_EditedAndSavedMultipleTimesInARow_NeverThrows()
    {
        // Regression test: editing and re-saving the same draft repeatedly
        // used to throw DbUpdateConcurrencyException on the second (or
        // later) save - ICollection.Clear()'s implicit orphan-detection for
        // the old PurchaseItem rows was generating an UPDATE against a
        // stale/mismatched Id instead of the expected DELETE.
        var created = await _sut.SaveDraftAsync(MakeDraftRequest(quantity: 10, unitCost: 5));
        var purchaseId = created.Value.Id;

        var second = await _sut.SaveDraftAsync(MakeDraftRequest(purchaseId, quantity: 20, unitCost: 6));
        var third = await _sut.SaveDraftAsync(MakeDraftRequest(purchaseId, quantity: 30, unitCost: 7));
        var fourth = await _sut.SaveDraftAsync(MakeDraftRequest(purchaseId, quantity: 40, unitCost: 8));

        Assert.True(second.IsSuccess);
        Assert.True(third.IsSuccess);
        Assert.True(fourth.IsSuccess);
        Assert.Single(fourth.Value.Items);
        Assert.Equal(40m, fourth.Value.Items[0].Quantity);

        // Only ever one live item for this purchase - every prior edit's
        // item was genuinely deleted, not left behind as an orphan.
        var liveItemCount = await _context.PurchaseItems.CountAsync(i => i.PurchaseId == purchaseId);
        Assert.Equal(1, liveItemCount);
    }

    [Fact]
    public async Task SaveDraftAsync_WithNoItems_Fails()
    {
        var request = new SaveDraftPurchaseRequest(
            null, _supplier.Id, null, DateTimeOffset.UtcNow, null, Array.Empty<PurchaseItemRequest>());

        var result = await _sut.SaveDraftAsync(request);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task SaveDraftAsync_WithUnknownSupplier_Fails()
    {
        var request = MakeDraftRequest() with { SupplierId = Guid.NewGuid() };

        var result = await _sut.SaveDraftAsync(request);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task SaveDraftAsync_OnConfirmedPurchase_Fails()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest());
        await _sut.ConfirmPurchaseAsync(created.Value.Id);

        var result = await _sut.SaveDraftAsync(MakeDraftRequest(created.Value.Id));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task ConfirmPurchaseAsync_CreatesStockMovementAndUpdatesProductStock()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest(quantity: 25));

        var result = await _sut.ConfirmPurchaseAsync(created.Value.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(PurchaseStatus.Confirmed, result.Value.Status);
        Assert.Equal(25m, await _inventoryService.GetCurrentStockAsync(_product.Id));

        var movement = await _context.StockMovements.SingleAsync(m => m.ProductId == _product.Id);
        Assert.Equal(StockMovementType.PurchaseReceipt, movement.MovementType);
        Assert.Equal(StockReferenceType.Purchase, movement.ReferenceType);
        Assert.Equal(created.Value.Id, movement.ReferenceId);
    }

    [Fact]
    public async Task ConfirmPurchaseAsync_CalledTwice_IsIdempotent_NeverDoublesStock()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest(quantity: 25));

        var first = await _sut.ConfirmPurchaseAsync(created.Value.Id);
        var second = await _sut.ConfirmPurchaseAsync(created.Value.Id);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsFailure);

        // Stock was only ever added once, despite two confirm attempts.
        Assert.Equal(25m, await _inventoryService.GetCurrentStockAsync(_product.Id));
        Assert.Equal(1, await _context.StockMovements.CountAsync(m => m.ProductId == _product.Id));
    }

    [Fact]
    public async Task CancelPurchaseAsync_FromDraft_Succeeds_WithNoStockMovements()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest());

        var result = await _sut.CancelPurchaseAsync(created.Value.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(PurchaseStatus.Cancelled, result.Value.Status);
        Assert.Equal(0, await _inventoryService.GetCurrentStockAsync(_product.Id));
        Assert.Empty(await _context.StockMovements.Where(m => m.ProductId == _product.Id).ToListAsync());
    }

    [Fact]
    public async Task CancelPurchaseAsync_FromConfirmed_ReversesStock()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest(quantity: 30));
        await _sut.ConfirmPurchaseAsync(created.Value.Id);

        var result = await _sut.CancelPurchaseAsync(created.Value.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(PurchaseStatus.Cancelled, result.Value.Status);
        Assert.Equal(0, await _inventoryService.GetCurrentStockAsync(_product.Id));

        var returnMovement = await _context.StockMovements
            .SingleAsync(m => m.ProductId == _product.Id && m.MovementType == StockMovementType.PurchaseReturn);
        Assert.Equal(-30m, returnMovement.QuantityChange);
    }

    [Fact]
    public async Task CancelPurchaseAsync_CalledTwice_IsIdempotent()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest());
        await _sut.CancelPurchaseAsync(created.Value.Id);

        var second = await _sut.CancelPurchaseAsync(created.Value.Id);

        Assert.True(second.IsFailure);
    }

    [Fact]
    public async Task CancelPurchaseAsync_WhenStockAlreadyPartlyConsumed_FailsWithNoEffect()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest(quantity: 30));
        await _sut.ConfirmPurchaseAsync(created.Value.Id);

        // Sell most of the received stock elsewhere, leaving less than the
        // purchase originally brought in.
        await _inventoryService.RemoveStockAsync(new RemoveStockRequest(
            _product.Id, 25m, StockMovementType.SaleIssue, StockReferenceType.Sale, Guid.NewGuid()));

        var result = await _sut.CancelPurchaseAsync(created.Value.Id);

        Assert.True(result.IsFailure);

        // Nothing about the cancellation was applied - status is still
        // Confirmed and stock is untouched by the failed cancel attempt.
        var reloaded = await _sut.GetPurchaseByIdAsync(created.Value.Id);
        Assert.Equal(PurchaseStatus.Confirmed, reloaded!.Status);
        Assert.Equal(5m, await _inventoryService.GetCurrentStockAsync(_product.Id));
    }

    [Fact]
    public async Task DeleteDraftAsync_OnDraft_Succeeds()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest());

        var result = await _sut.DeleteDraftAsync(created.Value.Id);

        Assert.True(result.IsSuccess);
        Assert.Null(await _sut.GetPurchaseByIdAsync(created.Value.Id));
    }

    [Fact]
    public async Task DeleteDraftAsync_OnConfirmedPurchase_Fails()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest());
        await _sut.ConfirmPurchaseAsync(created.Value.Id);

        var result = await _sut.DeleteDraftAsync(created.Value.Id);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task SetPaymentStatusAsync_UpdatesStatus()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest());

        var result = await _sut.SetPaymentStatusAsync(created.Value.Id, PurchasePaymentStatus.Paid);

        Assert.True(result.IsSuccess);
        Assert.Equal(PurchasePaymentStatus.Paid, result.Value.PaymentStatus);
    }

    [Fact]
    public async Task GetPurchasesAsync_FiltersByDateRange_DoesNotThrow()
    {
        // Regression test: SQLite's EF Core provider cannot translate a
        // WHERE comparison (>=, <=) on a DateTimeOffset column - this used
        // to throw InvalidOperationException the instant a From or To date
        // filter was applied.
        await _sut.SaveDraftAsync(MakeDraftRequest());

        var today = DateTimeOffset.UtcNow.Date;

        var withFromDate = await _sut.GetPurchasesAsync(
            new PurchaseQueryParameters { FromDate = today.AddDays(-1) });
        var withToDate = await _sut.GetPurchasesAsync(
            new PurchaseQueryParameters { ToDate = today.AddDays(1) });
        var excludedByFromDate = await _sut.GetPurchasesAsync(
            new PurchaseQueryParameters { FromDate = today.AddDays(1) });

        Assert.Single(withFromDate.Items);
        Assert.Single(withToDate.Items);
        Assert.Empty(excludedByFromDate.Items);
    }

    [Fact]
    public async Task GetPurchasesAsync_FiltersBySupplierAndStatus()
    {
        var otherSupplier = new Supplier { Name = "Other Supplier" };
        _context.Suppliers.Add(otherSupplier);
        await _context.SaveChangesAsync();

        var p1 = await _sut.SaveDraftAsync(MakeDraftRequest());
        await _sut.ConfirmPurchaseAsync(p1.Value.Id);

        var p2Request = MakeDraftRequest() with { SupplierId = otherSupplier.Id };
        await _sut.SaveDraftAsync(p2Request);

        var bySupplier = await _sut.GetPurchasesAsync(new PurchaseQueryParameters { SupplierId = _supplier.Id });
        var byStatus = await _sut.GetPurchasesAsync(new PurchaseQueryParameters { Status = PurchaseStatus.Confirmed });

        Assert.Single(bySupplier.Items);
        Assert.Single(byStatus.Items);
    }

    [Fact]
    public async Task GetPurchasesAsync_SearchMatchesPurchaseNumberAndSupplierInvoiceNumber()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest());

        var byPurchaseNumber = await _sut.GetPurchasesAsync(
            new PurchaseQueryParameters { SearchTerm = created.Value.PurchaseNumber });
        var byInvoiceNumber = await _sut.GetPurchasesAsync(
            new PurchaseQueryParameters { SearchTerm = "INV-001" });

        Assert.Single(byPurchaseNumber.Items);
        Assert.Single(byInvoiceNumber.Items);
    }

    [Fact]
    public async Task PurchaseNumbers_AreUniqueAcrossMultipleDrafts()
    {
        var first = await _sut.SaveDraftAsync(MakeDraftRequest());
        var second = await _sut.SaveDraftAsync(MakeDraftRequest());

        Assert.NotEqual(first.Value.PurchaseNumber, second.Value.PurchaseNumber);
    }

    [Fact]
    public async Task SaveDraftAsync_GeneratesPurchaseNumberWithInstallationTag()
    {
        // Decision 3: format is PO-<6 char tag>-<5 digit sequence>, e.g.
        // PO-A1B2C3-00001 - collision-safe across offline installs without
        // any server coordination, unlike the old local-COUNT-only scheme.
        var result = await _sut.SaveDraftAsync(MakeDraftRequest());

        Assert.Matches(@"^PO-[0-9A-F]{6}-\d{5}$", result.Value.PurchaseNumber);
    }

    [Fact]
    public async Task SaveDraftAsync_ReusesTheSameInstallationTag_AcrossMultiplePurchases()
    {
        var first = await _sut.SaveDraftAsync(MakeDraftRequest());
        var second = await _sut.SaveDraftAsync(MakeDraftRequest());

        var firstTag = first.Value.PurchaseNumber.Split('-')[1];
        var secondTag = second.Value.PurchaseNumber.Split('-')[1];

        Assert.Equal(firstTag, secondTag);
    }

    [Fact]
    public async Task SaveDraftAsync_PersistsTheInstallationTag_AsAnApplicationSetting()
    {
        await _sut.SaveDraftAsync(MakeDraftRequest());

        var setting = await _context.ApplicationSettings.SingleOrDefaultAsync(s => s.Key == "ClientInstallationTag");

        Assert.NotNull(setting);
        Assert.False(string.IsNullOrWhiteSpace(setting!.Value));
    }

    [Fact]
    public async Task SaveDraftAsync_NewPurchase_AtomicallyQueuesAnOutboxOperation()
    {
        // Decision 8: a brand-new purchase's business data and its Outbox
        // entry are committed in the SAME transaction - this test verifies
        // the observable result (both exist afterward), which is the part
        // that actually matters; true all-or-nothing atomicity additionally
        // depends on SQLite's own transactional guarantees for a single
        // SaveChangesAsync call, which this test does not need to
        // separately prove.
        var result = await _sut.SaveDraftAsync(MakeDraftRequest());

        var outboxOperations = await _context.OutboxOperations
            .Where(o => o.EntityId == result.Value.Id)
            .ToListAsync();

        var operation = Assert.Single(outboxOperations);
        Assert.Equal("Purchase.Create", operation.OperationType);
        Assert.Equal(nameof(Purchase), operation.EntityType);
        Assert.Equal(OutboxOperationStatus.Pending, operation.Status);
        Assert.False(string.IsNullOrWhiteSpace(operation.PayloadJson));
    }

    [Fact]
    public async Task SaveDraftAsync_OutboxPayload_DeserializesToTheSubmittedItems()
    {
        var result = await _sut.SaveDraftAsync(MakeDraftRequest(quantity: 7, unitCost: 12));

        var operation = await _context.OutboxOperations.SingleAsync(o => o.EntityId == result.Value.Id);
        var contract = JsonSerializer.Deserialize<CreatePurchaseContract>(operation.PayloadJson);

        Assert.NotNull(contract);
        Assert.Equal(result.Value.Id, contract!.PurchaseId);
        Assert.Equal(_supplier.Id, contract.SupplierId);
        var item = Assert.Single(contract.Items);
        Assert.Equal(7m, item.Quantity);
        Assert.Equal(12m, item.UnitCost);
    }

    [Fact]
    public async Task SaveDraftAsync_EditingAnExistingDraft_DoesNotQueueAnAdditionalOutboxOperation()
    {
        // Only Purchase.Create is queued in this foundation step - editing
        // a draft has no server-side sync handler yet to receive it (see
        // PurchaseService.EnqueueCreatePurchaseOutboxOperation's remarks).
        var created = await _sut.SaveDraftAsync(MakeDraftRequest());
        await _sut.SaveDraftAsync(MakeDraftRequest(created.Value.Id, quantity: 20));

        var outboxOperations = await _context.OutboxOperations
            .Where(o => o.EntityId == created.Value.Id)
            .ToListAsync();

        Assert.Single(outboxOperations);
    }

    [Fact]
    public async Task SaveDraftAsync_ValidationFailure_CreatesNeitherAPurchaseNorAnOutboxOperation()
    {
        // Local rollback/atomicity: a request that fails validation must
        // leave the database exactly as it was - no partial Purchase, and
        // critically, no "orphan" Outbox operation for a purchase that was
        // never actually saved. ValidateAsync runs and can fail before
        // ChangeTracker ever tracks anything new, so there is nothing for
        // SaveChangesAsync to even attempt to commit.
        var invalidRequest = MakeDraftRequest(quantity: -1); // fails ValidateItemShape

        var purchaseCountBefore = await _context.Purchases.IgnoreQueryFilters().CountAsync();
        var outboxCountBefore = await _context.OutboxOperations.CountAsync();

        var result = await _sut.SaveDraftAsync(invalidRequest);

        Assert.True(result.IsFailure);
        Assert.Equal(purchaseCountBefore, await _context.Purchases.IgnoreQueryFilters().CountAsync());
        Assert.Equal(outboxCountBefore, await _context.OutboxOperations.CountAsync());
    }

    [Fact]
    public async Task SaveDraftAsync_NewPurchase_NeverProducesAnOutboxOperationWithoutAMatchingPurchase()
    {
        // The inverse of the atomicity guarantee above, checked across
        // several creates: every OutboxOperation for a Purchase.Create
        // must reference a Purchase that genuinely exists.
        await _sut.SaveDraftAsync(MakeDraftRequest());
        await _sut.SaveDraftAsync(MakeDraftRequest());
        await _sut.SaveDraftAsync(MakeDraftRequest());

        var createOperations = await _context.OutboxOperations
            .Where(o => o.OperationType == "Purchase.Create")
            .ToListAsync();

        foreach (var operation in createOperations)
        {
            var purchaseExists = await _context.Purchases
                .IgnoreQueryFilters()
                .AnyAsync(p => p.Id == operation.EntityId);

            Assert.True(purchaseExists, $"OutboxOperation {operation.Id} references a Purchase that does not exist.");
        }
    }
}
