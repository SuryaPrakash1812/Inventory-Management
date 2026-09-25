using System.Text.Json;
using InventoryManagement.Contracts.Purchases;
using InventoryManagement.Contracts.Sync;
using InventoryManagement.Domain.Entities;
using InventoryManagement.Infrastructure.Postgres.Idempotency;
using InventoryManagement.Infrastructure.Postgres.Purchases;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InventoryManagement.Infrastructure.Postgres.Tests.Purchases;

/// <summary>
/// Covers two mandatory scenarios for PurchaseSyncService:
///
/// 1. Idempotency: create offline purchase, synchronize, simulate a
///    timeout after the server commits but before the client receives the
///    response, client retries with the SAME OperationId - server must
///    not create a second Purchase and must return the original result.
///
/// 2. Server-side validation: the server must independently re-validate
///    everything (supplier/product existence, item shape, totals) against
///    its own current data rather than trusting whatever the client
///    believed was true when it created the purchase offline.
///
/// Uses EF Core's InMemory provider rather than a real PostgreSQL
/// instance. This is a deliberate, scoped choice: the behavior under test
/// (check ProcessedOperations for this OperationId first; if found,
/// return the stored result without reprocessing; otherwise process and
/// record, atomically - and the plain validation logic) is
/// provider-agnostic application logic - it does not depend on anything
/// PostgreSQL-specific. What this test class explicitly does NOT verify,
/// and cannot, without a real PostgreSQL instance: xmin-based optimistic
/// concurrency, the purchase_number_seq sequence's real atomicity under
/// genuine concurrent load, and PostgreSQL's actual transaction isolation
/// guarantees. Those require integration testing against a real database -
/// see the migration report's Known Limitations and Manual Test
/// Instructions (TEST D, and the concurrency tests) for how to verify them
/// by hand once a real PostgreSQL instance is available.
/// </summary>
public class PurchaseSyncServiceIdempotencyTests
{
    private static (PurchaseSyncService Service, InventoryPostgresDbContext Context) CreateSut(string databaseName)
    {
        var options = new DbContextOptionsBuilder<InventoryPostgresDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        var context = new InventoryPostgresDbContext(options);
        var idempotencyStore = new IdempotencyStore(context);
        var numberGenerator = new SequentialFakePurchaseNumberGenerator();

        return (new PurchaseSyncService(context, idempotencyStore, numberGenerator), context);
    }

    private static SyncOperationRequest MakeCreateRequest(Guid operationId, Guid purchaseId, Guid supplierId, Guid productId)
    {
        var contract = new CreatePurchaseContract(
            purchaseId,
            supplierId,
            SupplierInvoiceNumber: "INV-100",
            PurchaseDate: DateTimeOffset.UtcNow,
            Notes: null,
            Items: new[] { new PurchaseItemContract(productId, Quantity: 5, UnitCost: 20, DiscountAmount: 0, TaxPercentage: 0) });

        return new SyncOperationRequest(
            operationId, "Purchase.Create", nameof(Purchase), purchaseId,
            DateTimeOffset.UtcNow, JsonSerializer.Serialize(contract));
    }

    [Fact]
    public async Task DuplicateOperationId_DoesNotCreateASecondPurchase_AndReturnsTheOriginalResult()
    {
        var (service, context) = CreateSut(nameof(DuplicateOperationId_DoesNotCreateASecondPurchase_AndReturnsTheOriginalResult));

        var supplier = new Supplier { Name = "Acme Supplies" };
        var category = new Category { Name = "General" };
        var product = new Product { Sku = "SKU-1", Name = "Widget", Category = category, CostPrice = 10, SellingPrice = 20 };
        context.Suppliers.Add(supplier);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var operationId = Guid.NewGuid();
        var purchaseId = Guid.NewGuid();
        var request = MakeCreateRequest(operationId, purchaseId, supplier.Id, product.Id);

        // First submission: processed for real.
        var firstResponse = await service.CreatePurchaseAsync(request);

        // Simulates the client never receiving the first response (e.g. a
        // network timeout after the server had already committed) and
        // retrying with the exact same OperationId - a fresh
        // SyncOperationRequest instance, not a reused one, to mirror what
        // an actual retried HTTP call would send.
        var retryRequest = MakeCreateRequest(operationId, purchaseId, supplier.Id, product.Id);
        var secondResponse = await service.CreatePurchaseAsync(retryRequest);

        Assert.Equal(SyncOperationOutcome.Processed, firstResponse.Outcome);
        Assert.Equal(SyncOperationOutcome.AlreadyProcessed, secondResponse.Outcome);

        // The mandatory guarantee: exactly one Purchase exists, and the
        // retry's result is byte-for-byte the original, not a freshly
        // recomputed one (which could legitimately differ, e.g. a new
        // PurchaseNumber, if this guarantee were broken).
        var allPurchases = await context.Purchases.ToListAsync();
        var singlePurchase = Assert.Single(allPurchases);
        Assert.Equal(purchaseId, singlePurchase.Id);

        Assert.Equal(firstResponse.ResultJson, secondResponse.ResultJson);
    }

    [Fact]
    public async Task DuplicateOperationId_DoesNotCallTheNumberGeneratorAgain()
    {
        // A stronger, more direct check than comparing ResultJson: proves
        // the second call genuinely short-circuits before doing any of the
        // "create a new purchase" work, rather than coincidentally
        // producing the same output some other way.
        var (service, context) = CreateSut(nameof(DuplicateOperationId_DoesNotCallTheNumberGeneratorAgain));

        var supplier = new Supplier { Name = "Acme Supplies" };
        var category = new Category { Name = "General" };
        var product = new Product { Sku = "SKU-1", Name = "Widget", Category = category, CostPrice = 10, SellingPrice = 20 };
        context.Suppliers.Add(supplier);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var idempotencyStore = new IdempotencyStore(context);
        var countingGenerator = new CountingFakePurchaseNumberGenerator();
        var trackedService = new PurchaseSyncService(context, idempotencyStore, countingGenerator);

        var operationId = Guid.NewGuid();
        var purchaseId = Guid.NewGuid();

        await trackedService.CreatePurchaseAsync(MakeCreateRequest(operationId, purchaseId, supplier.Id, product.Id));
        await trackedService.CreatePurchaseAsync(MakeCreateRequest(operationId, purchaseId, supplier.Id, product.Id));

        Assert.Equal(1, countingGenerator.CallCount);
    }

    [Fact]
    public async Task CreatePurchaseAsync_RejectsNonExistentSupplier_AndCreatesNothing()
    {
        // Server-side validation: never trust the client's belief that the
        // Supplier it referenced offline still exists - the server checks
        // its OWN current data.
        var (service, context) = CreateSut(nameof(CreatePurchaseAsync_RejectsNonExistentSupplier_AndCreatesNothing));

        var category = new Category { Name = "General" };
        var product = new Product { Sku = "SKU-1", Name = "Widget", Category = category, CostPrice = 10, SellingPrice = 20 };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var request = MakeCreateRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() /* nonexistent supplier */, product.Id);

        var response = await service.CreatePurchaseAsync(request);

        Assert.Equal(SyncOperationOutcome.ValidationFailed, response.Outcome);
        Assert.Contains("supplier", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await context.Purchases.ToListAsync());
    }

    [Fact]
    public async Task CreatePurchaseAsync_RejectsNonExistentProduct_AndCreatesNothing()
    {
        var (service, context) = CreateSut(nameof(CreatePurchaseAsync_RejectsNonExistentProduct_AndCreatesNothing));

        var supplier = new Supplier { Name = "Acme Supplies" };
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        var request = MakeCreateRequest(Guid.NewGuid(), Guid.NewGuid(), supplier.Id, Guid.NewGuid() /* nonexistent product */);

        var response = await service.CreatePurchaseAsync(request);

        Assert.Equal(SyncOperationOutcome.ValidationFailed, response.Outcome);
        Assert.Contains("product", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await context.Purchases.ToListAsync());
    }

    [Fact]
    public async Task CreatePurchaseAsync_RejectsInvalidQuantity_AndCreatesNothing()
    {
        var (service, context) = CreateSut(nameof(CreatePurchaseAsync_RejectsInvalidQuantity_AndCreatesNothing));

        var supplier = new Supplier { Name = "Acme Supplies" };
        var category = new Category { Name = "General" };
        var product = new Product { Sku = "SKU-1", Name = "Widget", Category = category, CostPrice = 10, SellingPrice = 20 };
        context.Suppliers.Add(supplier);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var purchaseId = Guid.NewGuid();
        var contract = new CreatePurchaseContract(
            purchaseId, supplier.Id, "INV-1", DateTimeOffset.UtcNow, null,
            new[] { new PurchaseItemContract(product.Id, Quantity: 0, UnitCost: 10, DiscountAmount: 0, TaxPercentage: 0) });
        var request = new SyncOperationRequest(
            Guid.NewGuid(), "Purchase.Create", nameof(Purchase), purchaseId,
            DateTimeOffset.UtcNow, JsonSerializer.Serialize(contract));

        var response = await service.CreatePurchaseAsync(request);

        Assert.Equal(SyncOperationOutcome.ValidationFailed, response.Outcome);
        Assert.Equal("Item quantity must be greater than zero.", response.ErrorMessage);
        Assert.Empty(await context.Purchases.ToListAsync());
    }

    [Fact]
    public async Task CreatePurchaseAsync_ValidRequest_CalculatesAuthoritativeTotals_IgnoringWhateverClientMightHaveSent()
    {
        // "Never trust totals calculated by WPF" - this test proves the
        // server computes its OWN totals from the raw line figures, since
        // CreatePurchaseContract does not even have a totals field for the
        // client to send in the first place.
        var (service, context) = CreateSut(nameof(CreatePurchaseAsync_ValidRequest_CalculatesAuthoritativeTotals_IgnoringWhateverClientMightHaveSent));

        var supplier = new Supplier { Name = "Acme Supplies" };
        var category = new Category { Name = "General" };
        var product = new Product { Sku = "SKU-1", Name = "Widget", Category = category, CostPrice = 10, SellingPrice = 20 };
        context.Suppliers.Add(supplier);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var purchaseId = Guid.NewGuid();
        var contract = new CreatePurchaseContract(
            purchaseId, supplier.Id, "INV-1", DateTimeOffset.UtcNow, null,
            new[] { new PurchaseItemContract(product.Id, Quantity: 3, UnitCost: 50, DiscountAmount: 10, TaxPercentage: 10) });
        var request = new SyncOperationRequest(
            Guid.NewGuid(), "Purchase.Create", nameof(Purchase), purchaseId,
            DateTimeOffset.UtcNow, JsonSerializer.Serialize(contract));

        await service.CreatePurchaseAsync(request);

        var saved = await context.Purchases.SingleAsync();
        // 3 * 50 = 150 subtotal, -10 discount = 140 taxable, +10% tax = 14, total = 154
        Assert.Equal(150m, saved.Subtotal);
        Assert.Equal(10m, saved.DiscountAmount);
        Assert.Equal(14m, saved.TaxAmount);
        Assert.Equal(154m, saved.TotalAmount);
    }

    private sealed class SequentialFakePurchaseNumberGenerator : IPurchaseNumberGenerator
    {
        private int _next = 1;

        public Task<string> GenerateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult($"PO-TEST-{_next++:D6}");
    }

    private sealed class CountingFakePurchaseNumberGenerator : IPurchaseNumberGenerator
    {
        public int CallCount { get; private set; }

        public Task<string> GenerateAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult($"PO-TEST-{CallCount:D6}");
        }
    }
}
