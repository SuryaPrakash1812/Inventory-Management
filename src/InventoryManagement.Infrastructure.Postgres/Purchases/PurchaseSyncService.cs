using System.Text.Json;
using InventoryManagement.Contracts.Purchases;
using InventoryManagement.Contracts.Sync;
using InventoryManagement.Domain.Entities;
using InventoryManagement.Domain.Enums;
using InventoryManagement.Domain.Purchases;
using InventoryManagement.Infrastructure.Postgres.Idempotency;
using Microsoft.EntityFrameworkCore;
// InventoryPostgresDbContext lives in the parent InventoryManagement.Infrastructure.Postgres
// namespace, not this .Purchases sub-namespace - needs its own explicit using.
using InventoryManagement.Infrastructure.Postgres;

namespace InventoryManagement.Infrastructure.Postgres.Purchases;

/// <summary>
/// Processes a "create Purchase" sync operation from a client. This is the
/// server-side counterpart to InventoryManagement.Infrastructure's
/// PurchaseService - both call the SAME InventoryManagement.Domain.
/// Purchases.PurchaseWorkflow rules for validation shape and total
/// computation, so the two can never silently drift apart. What's
/// DIFFERENT here, deliberately: this method NEVER trusts anything the
/// client computed (totals, or the client's belief that a Supplier/
/// Product still exists) - it independently re-derives everything from
/// the server's own current PostgreSQL state, exactly as a synchronization
/// endpoint must.
///
/// Scope note: this handles Purchase creation only (Confirm/Cancel sync
/// handlers are not part of this foundation step - see the migration
/// report). It is also not wired to anything that calls it automatically;
/// nothing in the WPF app's outbox currently triggers a real HTTP call to
/// this endpoint yet (Decision: architecture foundation, not the complete
/// Sync Engine).
/// </summary>
public sealed class PurchaseSyncService
{
    private readonly InventoryPostgresDbContext _context;
    private readonly IIdempotencyStore _idempotencyStore;

    public PurchaseSyncService(InventoryPostgresDbContext context, IIdempotencyStore idempotencyStore)
    {
        _context = context;
        _idempotencyStore = idempotencyStore;
    }

    public async Task<SyncOperationResponse> CreatePurchaseAsync(
        SyncOperationRequest request, CancellationToken cancellationToken = default)
    {
        // Decision 9: a duplicate OperationId returns the ORIGINAL result,
        // never reprocesses. Checked first, before any validation or
        // deserialization of the new request.
        var alreadyProcessed = await _idempotencyStore.FindAsync(request.OperationId, cancellationToken);
        if (alreadyProcessed is not null)
        {
            return new SyncOperationResponse(
                request.OperationId, SyncOperationOutcome.AlreadyProcessed, null, alreadyProcessed.ResultJson);
        }

        CreatePurchaseContract? contract;
        try
        {
            contract = JsonSerializer.Deserialize<CreatePurchaseContract>(request.PayloadJson);
        }
        catch (JsonException)
        {
            return Failure(request.OperationId, "Malformed purchase payload.");
        }

        if (contract is null)
        {
            return Failure(request.OperationId, "Empty purchase payload.");
        }

        var hasItemsCheck = PurchaseWorkflow.ValidateHasItems(contract.Items.Count);
        if (hasItemsCheck.IsFailure)
        {
            return Failure(request.OperationId, hasItemsCheck.Error!);
        }

        // Independent existence check against the server's OWN current
        // data - the client's local SQLite copy of this Supplier could be
        // stale or, in a real conflict scenario, no longer exist server-side
        // at all by the time this sync reaches the server.
        var supplierExists = await _context.Suppliers.AnyAsync(s => s.Id == contract.SupplierId, cancellationToken);
        if (!supplierExists)
        {
            return Failure(request.OperationId, "Referenced supplier does not exist on the server.");
        }

        var lineComputations = new List<PurchaseWorkflow.LineComputation>(contract.Items.Count);
        var purchaseItems = new List<PurchaseItem>(contract.Items.Count);

        foreach (var item in contract.Items)
        {
            var shapeCheck = PurchaseWorkflow.ValidateItemShape(
                item.Quantity, item.UnitCost, item.DiscountAmount, item.TaxPercentage);
            if (shapeCheck.IsFailure)
            {
                return Failure(request.OperationId, shapeCheck.Error!);
            }

            var productExists = await _context.Products.AnyAsync(p => p.Id == item.ProductId, cancellationToken);
            if (!productExists)
            {
                return Failure(request.OperationId, "One of the items refers to a product that does not exist on the server.");
            }

            // Server independently recomputes every total from the raw
            // quantity/price/discount/tax figures - it never trusts
            // whatever LineTotal the client may have sent.
            var computation = PurchaseWorkflow.ComputeLine(
                item.Quantity, item.UnitCost, item.DiscountAmount, item.TaxPercentage);
            lineComputations.Add(computation);

            purchaseItems.Add(new PurchaseItem
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitCost = item.UnitCost,
                DiscountAmount = item.DiscountAmount,
                TaxPercentage = item.TaxPercentage,
                TaxAmount = computation.TaxAmount,
                LineTotal = computation.LineTotal,
            });
        }

        var headerTotals = PurchaseWorkflow.ComputeHeaderTotals(lineComputations);
        var purchaseNumber = await GenerateServerPurchaseNumberAsync(cancellationToken);

        var purchase = new Purchase
        {
            // Decision 3: the client-generated GUID (Purchase.Id, assigned
            // offline at construction) is kept as authoritative here too -
            // GUIDs are already globally safe, so there is no reason to
            // mint a different Id server-side. Setting Id explicitly
            // requires Domain's InternalsVisibleTo grant to this project.
            Id = contract.PurchaseId,
            PurchaseNumber = purchaseNumber,
            SupplierId = contract.SupplierId,
            SupplierInvoiceNumber = contract.SupplierInvoiceNumber,
            PurchaseDate = contract.PurchaseDate,
            Notes = contract.Notes,
            Status = PurchaseStatus.Draft,
            PaymentStatus = PurchasePaymentStatus.Unpaid,
            Subtotal = headerTotals.Subtotal,
            DiscountAmount = headerTotals.DiscountTotal,
            TaxAmount = headerTotals.TaxTotal,
            TotalAmount = headerTotals.TotalAmount,
        };

        foreach (var item in purchaseItems)
        {
            item.PurchaseId = purchase.Id;
        }

        _context.Purchases.Add(purchase);
        _context.PurchaseItems.AddRange(purchaseItems);

        var resultContract = new PurchaseSyncResultContract(purchase.Id, purchase.PurchaseNumber, purchase.Status);
        var resultJson = JsonSerializer.Serialize(resultContract);

        // Recorded in the SAME context as the business data above - one
        // SaveChangesAsync call below commits both atomically, mirroring
        // the client-side "outbox + business data, one transaction"
        // pattern (Decision 8).
        await _idempotencyStore.RecordAsync(
            request.OperationId, "Purchase.Create", nameof(Purchase), purchase.Id,
            resultJson, DateTimeOffset.UtcNow, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        return new SyncOperationResponse(request.OperationId, SyncOperationOutcome.Processed, null, resultJson);
    }

    /// <summary>
    /// Simple sequential scheme for this foundation step - matches the
    /// existing local scheme's shape (see PurchaseService.
    /// GenerateNextPurchaseNumberAsync's remarks) but with a server-specific
    /// tag rather than a per-install one, since exactly one server exists.
    /// Under genuinely concurrent request load, a COUNT-then-insert has a
    /// narrow race window between two simultaneous requests; a real
    /// Postgres SEQUENCE would close that window entirely and is the
    /// natural next refinement, deliberately not built out in this
    /// foundation step.
    /// </summary>
    private async Task<string> GenerateServerPurchaseNumberAsync(CancellationToken cancellationToken)
    {
        var count = await _context.Purchases.IgnoreQueryFilters().CountAsync(cancellationToken);
        return PurchaseWorkflow.FormatPurchaseNumber("SRV", count + 1);
    }

    private static SyncOperationResponse Failure(Guid operationId, string errorMessage) =>
        new(operationId, SyncOperationOutcome.ValidationFailed, errorMessage, null);
}
