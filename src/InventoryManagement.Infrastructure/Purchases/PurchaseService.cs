using System.Text.Json;
using InventoryManagement.Application.Auth;
using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Application.Inventory;
using InventoryManagement.Application.Purchases;
using InventoryManagement.Contracts.Purchases;
using InventoryManagement.Core.Common;
using InventoryManagement.Domain.Entities;
using InventoryManagement.Domain.Enums;
using InventoryManagement.Domain.Purchases;
using InventoryManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Infrastructure.Purchases;

public sealed class PurchaseService : IPurchaseService
{
    private readonly IAppDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly IAuditLogger _auditLogger;
    private readonly IDateTimeProvider _dateTimeProvider;

    public PurchaseService(
        IAppDbContext context,
        IInventoryService inventoryService,
        IAuditLogger auditLogger,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _inventoryService = inventoryService;
        _auditLogger = auditLogger;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<PagedResult<PurchaseSummary>> GetPurchasesAsync(
        PurchaseQueryParameters query, CancellationToken cancellationToken = default)
    {
        var filtered = BuildFilteredQuery(query);

        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = query.PageSize <= 0 ? 25 : query.PageSize;

        // SQLite cannot translate ORDER BY on a DateTimeOffset column (see
        // ProductService.ApplySort remarks for the same limitation
        // elsewhere in this codebase), and it turns out it cannot translate
        // WHERE comparisons (>=, <=) on one either - both PurchaseDate
        // sorting AND the From/To date filter below have to happen in
        // memory rather than as part of the SQL query. Everything else
        // (search term, supplier, status) is still filtered server-side in
        // BuildFilteredQuery.
        var all = await filtered
            .Select(p => new PurchaseSummary(
                p.Id, p.PurchaseNumber, p.SupplierInvoiceNumber, p.SupplierId, p.Supplier.Name,
                p.PurchaseDate, p.Status, p.PaymentStatus, p.TotalAmount))
            .ToListAsync(cancellationToken);

        IEnumerable<PurchaseSummary> dateFiltered = all;
        if (query.FromDate is { } fromDate)
        {
            dateFiltered = dateFiltered.Where(p => p.PurchaseDate >= fromDate);
        }

        if (query.ToDate is { } toDate)
        {
            dateFiltered = dateFiltered.Where(p => p.PurchaseDate <= toDate);
        }

        var dateFilteredList = dateFiltered.ToList();
        var totalCount = dateFilteredList.Count;

        var sorted = query.SortDescending
            ? dateFilteredList.OrderByDescending(p => p.PurchaseDate)
            : dateFilteredList.OrderBy(p => p.PurchaseDate);

        var items = sorted.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();

        return new PagedResult<PurchaseSummary>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<PurchaseDetail?> GetPurchaseByIdAsync(
        Guid purchaseId, CancellationToken cancellationToken = default)
    {
        // AsNoTracking: this is a read-only "view" query. Every mutating
        // method (SaveDraftAsync, ConfirmPurchaseAsync, CancelPurchaseAsync)
        // does its own separate tracked load when it actually needs to
        // modify something - this one must never leave tracked entities
        // behind in the shared, long-lived DbContext, or a later mutation's
        // own load could collide with stale state left over from simply
        // having opened the purchase to look at it earlier in the session.
        var purchase = await _context.Purchases
            .AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Items)
            .ThenInclude(i => i.Product)
            .SingleOrDefaultAsync(p => p.Id == purchaseId, cancellationToken);

        return purchase is null ? null : ToDetail(purchase);
    }

    public async Task<Result<PurchaseDetail>> SaveDraftAsync(
        SaveDraftPurchaseRequest request, CancellationToken cancellationToken = default)
    {
        // See IAppDbContext.ChangeTracker remarks: guarantees this method's
        // own queries start from a clean slate, regardless of what an
        // earlier operation in this session left tracked.
        _context.ChangeTracker.Clear();

        var validationError = await ValidateAsync(request.SupplierId, request.Items, cancellationToken);
        if (validationError is not null)
        {
            return Result.Failure<PurchaseDetail>(validationError);
        }

        Purchase purchase;
        AuditAction auditAction;

        if (request.PurchaseId is { } existingId)
        {
            var existing = await _context.Purchases
                .SingleOrDefaultAsync(p => p.Id == existingId, cancellationToken);

            if (existing is null)
            {
                return Result.Failure<PurchaseDetail>("Purchase not found.");
            }

            var editGuard = PurchaseWorkflow.EnsureEditable(existing.Status);
            if (editGuard.IsFailure)
            {
                return Result.Failure<PurchaseDetail>(editGuard.Error!);
            }

            // Bulk-delete the old items directly against the database,
            // completely bypassing the change tracker - it never loads or
            // tracks the affected rows at all, so there is nothing left
            // over for anything added later in this method to collide with.
            await _context.PurchaseItems
                .Where(i => i.PurchaseId == existingId)
                .ExecuteDeleteAsync(cancellationToken);

            purchase = existing;
            auditAction = AuditAction.Updated;
        }
        else
        {
            purchase = new Purchase
            {
                PurchaseNumber = await GenerateNextPurchaseNumberAsync(cancellationToken),
                Status = PurchaseStatus.Draft,
                PaymentStatus = PurchasePaymentStatus.Unpaid,
            };
            _context.Purchases.Add(purchase);
            auditAction = AuditAction.Created;
        }

        purchase.SupplierId = request.SupplierId;
        purchase.SupplierInvoiceNumber = request.SupplierInvoiceNumber;
        purchase.PurchaseDate = request.PurchaseDate;
        purchase.Notes = request.Notes;

        decimal subtotal = 0, discountTotal = 0, taxTotal = 0;

        // Every field, including the PurchaseId foreign key, is set
        // explicitly here rather than left to EF Core's relationship-fixup
        // (which happens automatically when adding to a tracked navigation
        // collection like purchase.Items). Each new item is also added
        // directly to _context.PurchaseItems - its own DbSet - rather than
        // via purchase.Items.Add(...). Three previous approaches all relied
        // on some combination of navigation-collection tracking or implicit
        // fixup and all corrupted EF Core's tracking state for this exact
        // relationship in the same way; this version depends on none of
        // that machinery. purchase.Id is safe to reference here even for a
        // brand-new Purchase that hasn't been saved yet, since BaseEntity
        // assigns its Guid at construction time, not on save.
        foreach (var itemRequest in request.Items)
        {
            var computation = PurchaseWorkflow.ComputeLine(
                itemRequest.Quantity, itemRequest.UnitCost, itemRequest.DiscountAmount, itemRequest.TaxPercentage);

            var newItem = new PurchaseItem
            {
                PurchaseId = purchase.Id,
                ProductId = itemRequest.ProductId,
                Quantity = itemRequest.Quantity,
                UnitCost = itemRequest.UnitCost,
                DiscountAmount = itemRequest.DiscountAmount,
                TaxPercentage = itemRequest.TaxPercentage,
                TaxAmount = computation.TaxAmount,
                LineTotal = computation.LineTotal,
            };

            _context.PurchaseItems.Add(newItem);

            subtotal += computation.LineSubtotal;
            discountTotal += computation.DiscountAmount;
            taxTotal += computation.TaxAmount;
        }

        purchase.Subtotal = subtotal;
        purchase.DiscountAmount = discountTotal;
        purchase.TaxAmount = taxTotal;
        purchase.TotalAmount = subtotal - discountTotal + taxTotal;

        // Section 11 (offline-completeness stage): every operation that
        // modifies a Purchase queues an Outbox entry now, not just
        // creation - there must be no silent case where SQLite changes but
        // has no representation for future synchronization. Only
        // Purchase.Create has a server-side handler to actually process
        // right now (PurchaseSyncService in Infrastructure.Postgres); the
        // others accumulate as Pending until later stages add their
        // handlers - see PurchaseSyncCoverage's coverage table, and
        // SyncEngine's remarks on why an unsupported OperationType is left
        // Pending rather than marked Failed.
        EnqueueOutboxOperation(
            auditAction == AuditAction.Created ? "Purchase.Create" : "Purchase.Update",
            purchase.Id,
            auditAction == AuditAction.Created
                ? new CreatePurchaseContract(
                    purchase.Id, purchase.SupplierId, purchase.SupplierInvoiceNumber, purchase.PurchaseDate,
                    purchase.Notes,
                    request.Items.Select(i => new PurchaseItemContract(i.ProductId, i.Quantity, i.UnitCost, i.DiscountAmount, i.TaxPercentage)).ToList())
                : new UpdatePurchaseContract(
                    purchase.Id, purchase.SupplierId, purchase.SupplierInvoiceNumber, purchase.PurchaseDate,
                    purchase.Notes,
                    request.Items.Select(i => new PurchaseItemContract(i.ProductId, i.Quantity, i.UnitCost, i.DiscountAmount, i.TaxPercentage)).ToList()));

        await _auditLogger.LogAsync(
            auditAction, nameof(Purchase), purchase.Id,
            $"Purchase '{purchase.PurchaseNumber}' draft saved with {request.Items.Count} item(s).", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToDetailByIdAsync(purchase.Id, cancellationToken));
    }

    /// <summary>
    /// One shared queuing method for every Purchase-mutating operation,
    /// replacing what was previously a Create-only helper. This entity's
    /// own Id (assigned at construction, per BaseEntity) IS the OperationId
    /// sent to the server and checked against its idempotency store - see
    /// OutboxOperation's own remarks.
    /// </summary>
    private void EnqueueOutboxOperation(string operationType, Guid purchaseId, object payload)
    {
        _context.OutboxOperations.Add(new OutboxOperation
        {
            OperationType = operationType,
            EntityType = nameof(Purchase),
            EntityId = purchaseId,
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedAtUtc = _dateTimeProvider.UtcNow,
            Status = OutboxOperationStatus.Pending,
        });
    }

    public async Task<Result> DeleteDraftAsync(Guid purchaseId, CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Clear();

        var purchase = await _context.Purchases.SingleOrDefaultAsync(p => p.Id == purchaseId, cancellationToken);
        if (purchase is null)
        {
            return Result.Failure("Purchase not found.");
        }

        var deleteGuard = PurchaseWorkflow.EnsureDeletable(purchase.Status);
        if (deleteGuard.IsFailure)
        {
            return Result.Failure(deleteGuard.Error!);
        }

        purchase.MarkDeleted(_dateTimeProvider.UtcNow, null);

        EnqueueOutboxOperation("Purchase.Delete", purchase.Id, new DeletePurchaseContract(purchase.Id));

        await _auditLogger.LogAsync(
            AuditAction.Deleted, nameof(Purchase), purchase.Id,
            $"Draft purchase '{purchase.PurchaseNumber}' deleted.", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<PurchaseDetail>> ConfirmPurchaseAsync(
        Guid purchaseId, CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Clear();

        var purchase = await _context.Purchases
            .Include(p => p.Items)
            .SingleOrDefaultAsync(p => p.Id == purchaseId, cancellationToken);

        if (purchase is null)
        {
            return Result.Failure<PurchaseDetail>("Purchase not found.");
        }

        // This check is what makes confirming idempotent: once Status is no
        // longer Draft, a second call always fails here before touching any
        // stock, so the same purchase can never have its stock added twice.
        var confirmGuard = PurchaseWorkflow.EnsureConfirmable(purchase.Status, purchase.Items.Count);
        if (confirmGuard.IsFailure)
        {
            return Result.Failure<PurchaseDetail>(confirmGuard.Error!);
        }

        foreach (var item in purchase.Items)
        {
            var addResult = await _inventoryService.AddStockAsync(new AddStockRequest(
                item.ProductId,
                item.Quantity,
                StockMovementType.PurchaseReceipt,
                StockReferenceType.Purchase,
                purchase.Id,
                $"Purchase {purchase.PurchaseNumber} confirmed"), cancellationToken);

            if (addResult.IsFailure)
            {
                return Result.Failure<PurchaseDetail>(
                    $"Failed to update stock for one of the items: {addResult.Error}");
            }
        }

        purchase.Status = PurchaseStatus.Confirmed;

        EnqueueOutboxOperation("Purchase.Confirm", purchase.Id, new ConfirmPurchaseContract(purchase.Id));

        await _auditLogger.LogAsync(
            AuditAction.Updated, nameof(Purchase), purchase.Id,
            $"Purchase '{purchase.PurchaseNumber}' confirmed - stock updated for {purchase.Items.Count} item(s).",
            cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToDetailByIdAsync(purchase.Id, cancellationToken));
    }

    public async Task<Result<PurchaseDetail>> CancelPurchaseAsync(
        Guid purchaseId, CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Clear();

        var purchase = await _context.Purchases
            .Include(p => p.Items)
            .SingleOrDefaultAsync(p => p.Id == purchaseId, cancellationToken);

        if (purchase is null)
        {
            return Result.Failure<PurchaseDetail>("Purchase not found.");
        }

        // Same idempotency guard as Confirm - a purchase can only ever be
        // cancelled once.
        var cancelGuard = PurchaseWorkflow.EnsureCancellable(purchase.Status);
        if (cancelGuard.IsFailure)
        {
            return Result.Failure<PurchaseDetail>(cancelGuard.Error!);
        }

        if (purchase.Status == PurchaseStatus.Confirmed)
        {
            // Safe business rule: never partially reverse or force stock
            // negative. Every line must have enough remaining stock to fully
            // reverse before ANY of them are touched.
            foreach (var item in purchase.Items)
            {
                var currentStock = await _inventoryService.GetCurrentStockAsync(item.ProductId, cancellationToken);

                var stockGuard = PurchaseWorkflow.EnsureSufficientStockToReverse(currentStock, item.Quantity);
                if (stockGuard.IsFailure)
                {
                    return Result.Failure<PurchaseDetail>(stockGuard.Error!);
                }
            }

            foreach (var item in purchase.Items)
            {
                var removeResult = await _inventoryService.RemoveStockAsync(new RemoveStockRequest(
                    item.ProductId,
                    item.Quantity,
                    StockMovementType.PurchaseReturn,
                    StockReferenceType.Purchase,
                    purchase.Id,
                    $"Purchase {purchase.PurchaseNumber} cancelled"), cancellationToken);

                if (removeResult.IsFailure)
                {
                    return Result.Failure<PurchaseDetail>(
                        $"Failed to reverse stock for one of the items: {removeResult.Error}");
                }
            }
        }

        purchase.Status = PurchaseStatus.Cancelled;

        EnqueueOutboxOperation("Purchase.Cancel", purchase.Id, new CancelPurchaseContract(purchase.Id));

        await _auditLogger.LogAsync(
            AuditAction.Updated, nameof(Purchase), purchase.Id,
            $"Purchase '{purchase.PurchaseNumber}' cancelled.", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToDetailByIdAsync(purchase.Id, cancellationToken));
    }

    public async Task<Result<PurchaseDetail>> SetPaymentStatusAsync(
        Guid purchaseId, PurchasePaymentStatus paymentStatus, CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Clear();

        var purchase = await _context.Purchases.SingleOrDefaultAsync(p => p.Id == purchaseId, cancellationToken);
        if (purchase is null)
        {
            return Result.Failure<PurchaseDetail>("Purchase not found.");
        }

        purchase.PaymentStatus = paymentStatus;

        EnqueueOutboxOperation(
            "Purchase.SetPaymentStatus", purchase.Id, new SetPurchasePaymentStatusContract(purchase.Id, paymentStatus));

        await _auditLogger.LogAsync(
            AuditAction.Updated, nameof(Purchase), purchase.Id,
            $"Purchase '{purchase.PurchaseNumber}' payment status set to {paymentStatus}.", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToDetailByIdAsync(purchase.Id, cancellationToken));
    }

    private IQueryable<Purchase> BuildFilteredQuery(PurchaseQueryParameters query)
    {
        var purchases = _context.Purchases.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim();
            purchases = purchases.Where(p =>
                p.PurchaseNumber.Contains(term)
                || (p.SupplierInvoiceNumber != null && p.SupplierInvoiceNumber.Contains(term)));
        }

        if (query.SupplierId is { } supplierId)
        {
            purchases = purchases.Where(p => p.SupplierId == supplierId);
        }

        if (query.Status is { } status)
        {
            purchases = purchases.Where(p => p.Status == status);
        }

        // FromDate/ToDate deliberately NOT applied here - see the
        // GetPurchasesAsync remarks on why they're filtered in memory
        // instead. SQLite cannot translate a >= / <= comparison on a
        // DateTimeOffset column into SQL.

        return purchases;
    }

    private async Task<string?> ValidateAsync(
        Guid supplierId, IReadOnlyList<PurchaseItemRequest> items, CancellationToken cancellationToken)
    {
        var hasItemsCheck = PurchaseWorkflow.ValidateHasItems(items.Count);
        if (hasItemsCheck.IsFailure)
        {
            return hasItemsCheck.Error;
        }

        var supplierExists = await _context.Suppliers.AnyAsync(s => s.Id == supplierId, cancellationToken);
        if (!supplierExists)
        {
            return "Selected supplier does not exist.";
        }

        foreach (var item in items)
        {
            var shapeCheck = PurchaseWorkflow.ValidateItemShape(
                item.Quantity, item.UnitCost, item.DiscountAmount, item.TaxPercentage);
            if (shapeCheck.IsFailure)
            {
                return shapeCheck.Error;
            }

            var productExists = await _context.Products.AnyAsync(p => p.Id == item.ProductId, cancellationToken);
            if (!productExists)
            {
                return "One of the items refers to a product that no longer exists.";
            }
        }

        return null;
    }

    private async Task<string> GenerateNextPurchaseNumberAsync(CancellationToken cancellationToken)
    {
        var clientTag = await GetOrCreateClientTagAsync(cancellationToken);

        // IgnoreQueryFilters: a soft-deleted draft must still count, or its
        // sequence number could be issued again to a later purchase on this
        // SAME install. The client tag (below) is what protects against
        // collisions ACROSS different installs - this still protects
        // against collisions within one install, same as before.
        var count = await _context.Purchases.IgnoreQueryFilters().CountAsync(cancellationToken);

        return PurchaseWorkflow.FormatPurchaseNumber(clientTag, count + 1);
    }

    /// <summary>
    /// Decision 3: PurchaseNumber must never collide across offline
    /// installs, and generating it must never require the API (Decision 4).
    /// A local-count-only scheme ("PO-00001") - the previous
    /// implementation - guarantees neither: two different installs both
    /// start counting from zero. This mints a short, installation-unique
    /// tag once (from a fresh GUID, entirely locally) and persists it in
    /// ApplicationSettings (already-existing local key/value table, no new
    /// table needed), so every purchase number generated on this install
    /// carries a tag no other install will ever generate. Preferred
    /// long-term approach per the architecture decision is server-assigned
    /// authoritative numbering at sync time; this is the interim (and
    /// possibly durable, pending review) scheme, chosen because it's fully
    /// self-contained and doesn't leave a purchase number permanently
    /// showing a placeholder until a sync engine that doesn't exist yet
    /// gets built - see the migration report for the full rationale.
    /// </summary>
    private async Task<string> GetOrCreateClientTagAsync(CancellationToken cancellationToken)
    {
        const string settingKey = "ClientInstallationTag";

        var existing = await _context.ApplicationSettings
            .SingleOrDefaultAsync(s => s.Key == settingKey, cancellationToken);

        if (existing?.Value is { Length: > 0 } value)
        {
            return value;
        }

        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        if (existing is not null)
        {
            existing.Value = tag;
        }
        else
        {
            _context.ApplicationSettings.Add(new ApplicationSetting
            {
                Key = settingKey,
                Value = tag,
                Description = "Short, installation-unique tag used in locally-generated Purchase numbers "
                    + "(see PurchaseWorkflow.FormatPurchaseNumber) so numbers generated on different "
                    + "offline installs can never collide. Generated once, on first use.",
            });
        }

        // Deliberately not calling SaveChangesAsync here - this setting row
        // rides along with the same SaveChangesAsync call that commits the
        // Purchase/PurchaseItems/AuditLog together at the end of
        // SaveDraftAsync, keeping the whole operation atomic rather than
        // introducing an early, separate commit.

        return tag;
    }

    private async Task<PurchaseDetail> ToDetailByIdAsync(Guid purchaseId, CancellationToken cancellationToken)
    {
        var purchase = await _context.Purchases
            .AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Items)
            .ThenInclude(i => i.Product)
            .SingleAsync(p => p.Id == purchaseId, cancellationToken);

        return ToDetail(purchase);
    }

    private static PurchaseDetail ToDetail(Purchase purchase)
    {
        var items = purchase.Items
            .Select(i => new PurchaseItemDetail(
                i.Id, i.ProductId, i.Product.Sku, i.Product.Name, i.Quantity, i.UnitCost,
                i.DiscountAmount, i.TaxPercentage, i.TaxAmount, i.LineTotal))
            .ToList();

        return new PurchaseDetail(
            purchase.Id, purchase.PurchaseNumber, purchase.SupplierInvoiceNumber, purchase.SupplierId,
            purchase.Supplier.Name, purchase.PurchaseDate, purchase.Status, purchase.PaymentStatus,
            purchase.Subtotal, purchase.DiscountAmount, purchase.TaxAmount, purchase.TotalAmount,
            purchase.Notes, items);
    }
}
