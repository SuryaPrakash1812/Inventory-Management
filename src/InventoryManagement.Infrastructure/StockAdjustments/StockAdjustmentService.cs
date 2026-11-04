using InventoryManagement.Application.Auth;
using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Application.Inventory;
using InventoryManagement.Application.StockAdjustments;
using InventoryManagement.Core.Common;
using InventoryManagement.Domain.Common;
using InventoryManagement.Domain.Entities;
using InventoryManagement.Domain.Enums;
using InventoryManagement.Domain.StockAdjustments;
using InventoryManagement.Infrastructure.Common;
using InventoryManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Infrastructure.StockAdjustments;

/// <summary>
/// Mirrors PurchaseService's structure deliberately - identical Draft ->
/// Confirmed -> Cancelled lifecycle, identical guard-then-mutate shape,
/// identical numbering strategy (collision-safe local tag, not
/// COUNT(*) alone). The one real difference: stock is affected via
/// IInventoryService.AdjustStockAsync (a single signed delta) rather than
/// AddStockAsync/RemoveStockAsync (Purchase always adds on confirm, never
/// a signed either-direction change).
/// </summary>
public sealed class StockAdjustmentService : IStockAdjustmentService
{
    private readonly IAppDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly IAuditLogger _auditLogger;
    private readonly IDateTimeProvider _dateTimeProvider;

    public StockAdjustmentService(
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

    public async Task<PagedResult<StockAdjustmentSummary>> GetStockAdjustmentsAsync(
        StockAdjustmentQueryParameters query, CancellationToken cancellationToken = default)
    {
        var filtered = BuildFilteredQuery(query);

        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = query.PageSize <= 0 ? 25 : query.PageSize;

        // Same SQLite DateTimeOffset ORDER BY/WHERE limitation documented
        // repeatedly elsewhere in this codebase (PurchaseService,
        // ProductService, OutboxProcessor) - sort and date-range filter
        // both happen in memory here for the same reason.
        var all = await filtered
            .Select(a => new StockAdjustmentSummary(
                a.Id, a.AdjustmentNumber, a.AdjustmentDate, a.Reason, a.Status, a.Items.Count))
            .ToListAsync(cancellationToken);

        IEnumerable<StockAdjustmentSummary> dateFiltered = all;
        if (query.FromDate is { } fromDate)
        {
            dateFiltered = dateFiltered.Where(a => a.AdjustmentDate >= fromDate);
        }

        if (query.ToDate is { } toDate)
        {
            dateFiltered = dateFiltered.Where(a => a.AdjustmentDate <= toDate);
        }

        var dateFilteredList = dateFiltered.ToList();
        var totalCount = dateFilteredList.Count;

        var sorted = query.SortDescending
            ? dateFilteredList.OrderByDescending(a => a.AdjustmentDate)
            : dateFilteredList.OrderBy(a => a.AdjustmentDate);

        var items = sorted.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();

        return new PagedResult<StockAdjustmentSummary>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<StockAdjustmentDetail?> GetStockAdjustmentByIdAsync(
        Guid stockAdjustmentId, CancellationToken cancellationToken = default)
    {
        var adjustment = await _context.StockAdjustments
            .AsNoTracking()
            .Include(a => a.Items)
            .ThenInclude(i => i.Product)
            .SingleOrDefaultAsync(a => a.Id == stockAdjustmentId, cancellationToken);

        return adjustment is null ? null : ToDetail(adjustment);
    }

    public async Task<Result<StockAdjustmentDetail>> SaveDraftAsync(
        SaveDraftStockAdjustmentRequest request, CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Clear();

        var validationError = await ValidateAsync(request.Items, cancellationToken);
        if (validationError is not null)
        {
            return Result.Failure<StockAdjustmentDetail>(validationError);
        }

        // The old items are bulk-deleted below, straight against the database:
        // without this, a failure in the final save would lose them.
        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        StockAdjustment adjustment;

        if (request.StockAdjustmentId is { } existingId)
        {
            var existing = await _context.StockAdjustments
                .SingleOrDefaultAsync(a => a.Id == existingId, cancellationToken);

            if (existing is null)
            {
                return Result.Failure<StockAdjustmentDetail>("Stock adjustment not found.");
            }

            var editGuard = StockAdjustmentWorkflow.EnsureEditable(existing.Status);
            if (editGuard.IsFailure)
            {
                return Result.Failure<StockAdjustmentDetail>(editGuard.Error!);
            }

            await _context.StockAdjustmentItems
                .Where(i => i.StockAdjustmentId == existingId)
                .ExecuteDeleteAsync(cancellationToken);

            adjustment = existing;
        }
        else
        {
            adjustment = new StockAdjustment
            {
                AdjustmentNumber = await GenerateNextAdjustmentNumberAsync(cancellationToken),
                Status = StockAdjustmentStatus.Draft,
            };
            _context.StockAdjustments.Add(adjustment);
        }

        adjustment.AdjustmentDate = request.AdjustmentDate;
        adjustment.Reason = request.Reason;
        adjustment.Notes = request.Notes;

        // QuantityBefore/After here are a PLANNED preview based on stock as
        // of right now - informational for the Draft screen only. If
        // confirmation happens later, ConfirmAsync recomputes and
        // overwrites both with the ACTUAL values at that moment, since
        // stock may have changed in between (another purchase, another
        // adjustment, etc.) - the stored record must reflect what
        // genuinely happened at confirm time, not what was merely planned.
        foreach (var itemRequest in request.Items)
        {
            var currentStock = await _inventoryService.GetCurrentStockAsync(itemRequest.ProductId, cancellationToken);

            _context.StockAdjustmentItems.Add(new StockAdjustmentItem
            {
                StockAdjustmentId = adjustment.Id,
                ProductId = itemRequest.ProductId,
                QuantityBefore = currentStock,
                QuantityAfter = currentStock + itemRequest.QuantityChange,
                QuantityChange = itemRequest.QuantityChange,
                Notes = itemRequest.Notes,
            });
        }

        await _auditLogger.LogAsync(
            request.StockAdjustmentId is null ? AuditAction.Created : AuditAction.Updated,
            nameof(StockAdjustment), adjustment.Id,
            $"Stock adjustment '{adjustment.AdjustmentNumber}' draft saved with {request.Items.Count} item(s).",
            cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Success(await ToDetailByIdAsync(adjustment.Id, cancellationToken));
    }

    public async Task<Result> DeleteDraftAsync(Guid stockAdjustmentId, CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Clear();

        var adjustment = await _context.StockAdjustments.SingleOrDefaultAsync(a => a.Id == stockAdjustmentId, cancellationToken);
        if (adjustment is null)
        {
            return Result.Failure("Stock adjustment not found.");
        }

        var deleteGuard = StockAdjustmentWorkflow.EnsureDeletable(adjustment.Status);
        if (deleteGuard.IsFailure)
        {
            return Result.Failure(deleteGuard.Error!);
        }

        adjustment.MarkDeleted(_dateTimeProvider.UtcNow, null);

        await _auditLogger.LogAsync(
            AuditAction.Deleted, nameof(StockAdjustment), adjustment.Id,
            $"Draft stock adjustment '{adjustment.AdjustmentNumber}' deleted.", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<StockAdjustmentDetail>> ConfirmAsync(
        Guid stockAdjustmentId, CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Clear();

        var adjustment = await _context.StockAdjustments
            .Include(a => a.Items)
            .SingleOrDefaultAsync(a => a.Id == stockAdjustmentId, cancellationToken);

        if (adjustment is null)
        {
            return Result.Failure<StockAdjustmentDetail>("Stock adjustment not found.");
        }

        var confirmGuard = StockAdjustmentWorkflow.EnsureConfirmable(adjustment.Status, adjustment.Items.Count);
        if (confirmGuard.IsFailure)
        {
            return Result.Failure<StockAdjustmentDetail>(confirmGuard.Error!);
        }

        // Each product's NET change across its lines is what must fit, so it
        // is verified before anything is touched.
        var products = await LoadProductStockAsync(adjustment, cancellationToken);
        foreach (var group in adjustment.Items.GroupBy(i => i.ProductId))
        {
            var (name, stock) = products[group.Key];
            var stockGuard = StockAdjustmentWorkflow.EnsureChangeKeepsStockNonNegative(stock, group.Sum(i => i.QuantityChange), name);
            if (stockGuard.IsFailure)
            {
                return Result.Failure<StockAdjustmentDetail>(stockGuard.Error!);
            }
        }

        // All-or-nothing: every stock movement and the status change commit
        // together. InventoryService joins this transaction, so a failure on
        // a later line also undoes the earlier ones - otherwise the adjustment
        // would still be Draft with part of it already applied, and confirming
        // again would apply that part twice.
        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        // Increases first, so offsetting lines for one product (-10 and +10)
        // never pass through a negative balance on the way to a valid total.
        foreach (var item in adjustment.Items.OrderByDescending(i => i.QuantityChange))
        {
            var adjustResult = await _inventoryService.AdjustStockAsync(new AdjustStockRequest(
                item.ProductId,
                item.QuantityChange,
                StockReferenceType.StockAdjustment,
                adjustment.Id,
                $"Stock adjustment {adjustment.AdjustmentNumber} confirmed ({adjustment.Reason})"), cancellationToken);

            if (adjustResult.IsFailure)
            {
                await transaction.RollbackAsync(cancellationToken);
                _context.ChangeTracker.Clear();
                return Result.Failure<StockAdjustmentDetail>(
                    $"Failed to apply stock change for {products[item.ProductId].Name}: {adjustResult.Error}");
            }

            // The ACTUAL values at this moment, not the possibly-stale preview
            // stored at draft time - see SaveDraftAsync's remarks.
            item.QuantityAfter = adjustResult.Value.QuantityBalanceAfter;
            item.QuantityBefore = item.QuantityAfter - item.QuantityChange;
        }

        adjustment.Status = StockAdjustmentStatus.Confirmed;

        await _auditLogger.LogAsync(
            AuditAction.Updated, nameof(StockAdjustment), adjustment.Id,
            $"Stock adjustment '{adjustment.AdjustmentNumber}' confirmed - stock updated for {adjustment.Items.Count} item(s).",
            cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Success(await ToDetailByIdAsync(adjustment.Id, cancellationToken));
    }

    public async Task<Result<StockAdjustmentDetail>> CancelAsync(
        Guid stockAdjustmentId, string? reason = null, CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Clear();

        var adjustment = await _context.StockAdjustments
            .Include(a => a.Items)
            .SingleOrDefaultAsync(a => a.Id == stockAdjustmentId, cancellationToken);

        if (adjustment is null)
        {
            return Result.Failure<StockAdjustmentDetail>("Stock adjustment not found.");
        }

        var cancelGuard = StockAdjustmentWorkflow.EnsureCancellable(adjustment.Status);
        if (cancelGuard.IsFailure)
        {
            return Result.Failure<StockAdjustmentDetail>(cancelGuard.Error!);
        }

        if (adjustment.Status == StockAdjustmentStatus.Confirmed)
        {
            // Reversing means applying the NEGATED original change. Before
            // touching anything, verify each product can be safely reversed
            // without going negative - same safe pattern as
            // PurchaseWorkflow.EnsureSufficientStockToReverse, generalized
            // for a signed change: a net increase needs enough stock to
            // remove; a net decrease just adds back, which can never fail.
            var products = await LoadProductStockAsync(adjustment, cancellationToken);
            foreach (var group in adjustment.Items.GroupBy(i => i.ProductId))
            {
                var netIncrease = group.Sum(i => i.QuantityChange);
                if (netIncrease <= 0)
                {
                    continue;
                }

                var (name, stock) = products[group.Key];
                var stockGuard = StockAdjustmentWorkflow.EnsureSufficientStockToReverse(stock, netIncrease, name);
                if (stockGuard.IsFailure)
                {
                    return Result.Failure<StockAdjustmentDetail>(stockGuard.Error!);
                }
            }
        }

        // All-or-nothing - see ConfirmAsync.
        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        if (adjustment.Status == StockAdjustmentStatus.Confirmed)
        {
            // Add-backs first (original decreases have the lowest change), so
            // offsetting lines never pass through a negative balance.
            foreach (var item in adjustment.Items.OrderBy(i => i.QuantityChange))
            {
                var reverseResult = await _inventoryService.AdjustStockAsync(new AdjustStockRequest(
                    item.ProductId,
                    -item.QuantityChange,
                    StockReferenceType.StockAdjustment,
                    adjustment.Id,
                    $"Stock adjustment {adjustment.AdjustmentNumber} cancelled"), cancellationToken);

                if (reverseResult.IsFailure)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    _context.ChangeTracker.Clear();
                    return Result.Failure<StockAdjustmentDetail>(
                        $"Failed to reverse stock for one of the items: {reverseResult.Error}");
                }
            }
        }

        adjustment.Status = StockAdjustmentStatus.Cancelled;

        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmedReason is not null)
        {
            adjustment.Notes = CancellationNote.Append(adjustment.Notes, trimmedReason, _dateTimeProvider.UtcNow);
        }

        await _auditLogger.LogAsync(
            AuditAction.Updated, nameof(StockAdjustment), adjustment.Id,
            trimmedReason is null
                ? $"Stock adjustment '{adjustment.AdjustmentNumber}' cancelled."
                : $"Stock adjustment '{adjustment.AdjustmentNumber}' cancelled: {trimmedReason}",
            cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Success(await ToDetailByIdAsync(adjustment.Id, cancellationToken));
    }

    private async Task<Dictionary<Guid, (string Name, decimal Stock)>> LoadProductStockAsync(
        StockAdjustment adjustment, CancellationToken cancellationToken)
    {
        var productIds = adjustment.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _context.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.QuantityOnHand })
            .ToListAsync(cancellationToken);

        return products.ToDictionary(p => p.Id, p => (p.Name, p.QuantityOnHand));
    }

    private IQueryable<StockAdjustment> BuildFilteredQuery(StockAdjustmentQueryParameters query)
    {
        var adjustments = _context.StockAdjustments.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var pattern = SearchPattern.Contains(query.SearchTerm.Trim());
            adjustments = adjustments.Where(a => EF.Functions.Like(a.AdjustmentNumber, pattern, SearchPattern.Escape));
        }

        if (query.Reason is { } reason)
        {
            adjustments = adjustments.Where(a => a.Reason == reason);
        }

        if (query.Status is { } status)
        {
            adjustments = adjustments.Where(a => a.Status == status);
        }

        return adjustments;
    }

    private async Task<string?> ValidateAsync(
        IReadOnlyList<StockAdjustmentItemRequest> items, CancellationToken cancellationToken)
    {
        var hasItemsCheck = StockAdjustmentWorkflow.ValidateHasItems(items.Count);
        if (hasItemsCheck.IsFailure)
        {
            return hasItemsCheck.Error;
        }

        foreach (var item in items)
        {
            var quantityCheck = StockAdjustmentWorkflow.ValidateItemQuantityChange(item.QuantityChange);
            if (quantityCheck.IsFailure)
            {
                return quantityCheck.Error;
            }

            var productExists = await _context.Products.AnyAsync(p => p.Id == item.ProductId, cancellationToken);
            if (!productExists)
            {
                return "One of the items refers to a product that no longer exists.";
            }
        }

        return null;
    }

    private async Task<string> GenerateNextAdjustmentNumberAsync(CancellationToken cancellationToken)
    {
        var clientTag = await GetOrCreateClientTagAsync(cancellationToken);
        var count = await _context.StockAdjustments.IgnoreQueryFilters().CountAsync(cancellationToken);

        return StockAdjustmentWorkflow.FormatAdjustmentNumber(clientTag, count + 1);
    }

    /// <summary>
    /// Reuses the SAME "ClientInstallationTag" ApplicationSetting
    /// PurchaseService already creates - one tag per install, shared
    /// across every document type that needs collision-safe local
    /// numbering, not a separate tag per module.
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
                Description = "Short, installation-unique tag used in locally-generated document numbers "
                    + "(Purchases, Stock Adjustments) so numbers generated on different offline installs "
                    + "can never collide. Generated once, on first use.",
            });
        }

        return tag;
    }

    private async Task<StockAdjustmentDetail> ToDetailByIdAsync(Guid stockAdjustmentId, CancellationToken cancellationToken)
    {
        var adjustment = await _context.StockAdjustments
            .AsNoTracking()
            .Include(a => a.Items)
            .ThenInclude(i => i.Product)
            .SingleAsync(a => a.Id == stockAdjustmentId, cancellationToken);

        return ToDetail(adjustment);
    }

    private static StockAdjustmentDetail ToDetail(StockAdjustment adjustment)
    {
        var items = adjustment.Items
            .Select(i => new StockAdjustmentItemDetail(
                i.Id, i.ProductId, i.Product.Sku, i.Product.Name,
                i.QuantityBefore, i.QuantityAfter, i.QuantityChange, i.Notes))
            .ToList();

        return new StockAdjustmentDetail(
            adjustment.Id, adjustment.AdjustmentNumber, adjustment.AdjustmentDate, adjustment.Reason,
            adjustment.Status, adjustment.Notes, items);
    }
}
