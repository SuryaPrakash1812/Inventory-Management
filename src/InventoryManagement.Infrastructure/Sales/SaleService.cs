using InventoryManagement.Application.Auth;
using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Application.Inventory;
using InventoryManagement.Application.Sales;
using InventoryManagement.Core.Common;
using InventoryManagement.Domain.Entities;
using InventoryManagement.Domain.Enums;
using InventoryManagement.Domain.Sales;
using InventoryManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Infrastructure.Sales;

public sealed class SaleService : ISaleService
{
    private readonly IAppDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly IAuditLogger _auditLogger;
    private readonly IDateTimeProvider _dateTimeProvider;

    public SaleService(
        IAppDbContext context, IInventoryService inventoryService, IAuditLogger auditLogger, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _inventoryService = inventoryService;
        _auditLogger = auditLogger;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<PagedResult<SaleSummary>> GetSalesAsync(
        SaleQueryParameters query, CancellationToken cancellationToken = default)
    {
        var filtered = _context.Sales.Include(s => s.Customer).AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim();
            filtered = filtered.Where(s =>
                s.SaleNumber.Contains(term) || (s.InvoiceNumber != null && s.InvoiceNumber.Contains(term)));
        }

        if (query.CustomerId is { } customerId)
        {
            filtered = filtered.Where(s => s.CustomerId == customerId);
        }

        if (query.Status is { } status)
        {
            filtered = filtered.Where(s => s.Status == status);
        }

        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = query.PageSize <= 0 ? 25 : query.PageSize;

        // Same SQLite DateTimeOffset limitation documented repeatedly
        // elsewhere in this codebase - sort and date-range filter happen
        // in memory.
        var all = await filtered
            .Select(s => new SaleSummary(s.Id, s.SaleNumber, s.InvoiceNumber, s.Customer.Name, s.SaleDate, s.Status, s.TotalAmount))
            .ToListAsync(cancellationToken);

        IEnumerable<SaleSummary> dateFiltered = all;
        if (query.FromDate is { } fromDate)
        {
            dateFiltered = dateFiltered.Where(s => s.SaleDate >= fromDate);
        }

        if (query.ToDate is { } toDate)
        {
            dateFiltered = dateFiltered.Where(s => s.SaleDate <= toDate);
        }

        var dateFilteredList = dateFiltered.ToList();
        var totalCount = dateFilteredList.Count;

        var sorted = query.SortDescending
            ? dateFilteredList.OrderByDescending(s => s.SaleDate)
            : dateFilteredList.OrderBy(s => s.SaleDate);

        var items = sorted.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();

        return new PagedResult<SaleSummary>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<SaleDetail?> GetSaleByIdAsync(Guid saleId, CancellationToken cancellationToken = default)
    {
        var sale = await _context.Sales
            .AsNoTracking()
            .Include(s => s.Customer)
            .Include(s => s.Items)
            .ThenInclude(i => i.Product)
            .SingleOrDefaultAsync(s => s.Id == saleId, cancellationToken);

        return sale is null ? null : ToDetail(sale);
    }

    public async Task<Result<SaleDetail>> SaveDraftAsync(
        SaveDraftSaleRequest request, CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Clear();

        var validationError = await ValidateAsync(request.CustomerId, request.Items, cancellationToken);
        if (validationError is not null)
        {
            return Result.Failure<SaleDetail>(validationError);
        }

        Sale sale;

        if (request.SaleId is { } existingId)
        {
            var existing = await _context.Sales.SingleOrDefaultAsync(s => s.Id == existingId, cancellationToken);
            if (existing is null)
            {
                return Result.Failure<SaleDetail>("Sale not found.");
            }

            var editGuard = SaleWorkflow.EnsureEditable(existing.Status);
            if (editGuard.IsFailure)
            {
                return Result.Failure<SaleDetail>(editGuard.Error!);
            }

            await _context.SaleItems.Where(i => i.SaleId == existingId).ExecuteDeleteAsync(cancellationToken);

            sale = existing;
        }
        else
        {
            sale = new Sale
            {
                SaleNumber = await GenerateNextNumberAsync("SaleNumberTag", SaleWorkflow.FormatSaleNumber, cancellationToken),
                Status = SaleStatus.Draft,
            };
            _context.Sales.Add(sale);
        }

        sale.CustomerId = request.CustomerId;
        sale.SaleDate = request.SaleDate;
        sale.Notes = request.Notes;

        decimal subtotal = 0, discountTotal = 0;

        foreach (var itemRequest in request.Items)
        {
            var computation = SaleWorkflow.ComputeLine(itemRequest.Quantity, itemRequest.UnitPrice, itemRequest.DiscountAmount);

            _context.SaleItems.Add(new SaleItem
            {
                SaleId = sale.Id,
                ProductId = itemRequest.ProductId,
                Quantity = itemRequest.Quantity,
                UnitPrice = itemRequest.UnitPrice,
                DiscountAmount = itemRequest.DiscountAmount,
                LineTotal = computation.LineTotal,
            });

            subtotal += computation.LineSubtotal;
            discountTotal += computation.DiscountAmount;
        }

        sale.Subtotal = subtotal;
        sale.DiscountAmount = discountTotal;
        sale.TaxAmount = 0m; // See SaleWorkflow's remarks - no per-line tax fields exist yet.
        sale.TotalAmount = subtotal - discountTotal;

        await _auditLogger.LogAsync(
            request.SaleId is null ? AuditAction.Created : AuditAction.Updated,
            nameof(Sale), sale.Id, $"Sale '{sale.SaleNumber}' draft saved with {request.Items.Count} item(s).", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToDetailByIdAsync(sale.Id, cancellationToken));
    }

    public async Task<Result> DeleteDraftAsync(Guid saleId, CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Clear();

        var sale = await _context.Sales.SingleOrDefaultAsync(s => s.Id == saleId, cancellationToken);
        if (sale is null)
        {
            return Result.Failure("Sale not found.");
        }

        var deleteGuard = SaleWorkflow.EnsureDeletable(sale.Status);
        if (deleteGuard.IsFailure)
        {
            return Result.Failure(deleteGuard.Error!);
        }

        sale.MarkDeleted(_dateTimeProvider.UtcNow, null);

        await _auditLogger.LogAsync(
            AuditAction.Deleted, nameof(Sale), sale.Id, $"Draft sale '{sale.SaleNumber}' deleted.", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<SaleDetail>> InvoiceAsync(Guid saleId, CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Clear();

        var sale = await _context.Sales.Include(s => s.Items).SingleOrDefaultAsync(s => s.Id == saleId, cancellationToken);
        if (sale is null)
        {
            return Result.Failure<SaleDetail>("Sale not found.");
        }

        var invoiceGuard = SaleWorkflow.EnsureInvoiceable(sale.Status, sale.Items.Count);
        if (invoiceGuard.IsFailure)
        {
            return Result.Failure<SaleDetail>(invoiceGuard.Error!);
        }

        // Unlike Purchase.Confirm (which only ever adds stock, nothing to
        // check), invoicing removes stock - every line is verified BEFORE
        // any of them are touched, so a failure partway through never
        // leaves some lines issued and others not.
        foreach (var item in sale.Items)
        {
            var currentStock = await _inventoryService.GetCurrentStockAsync(item.ProductId, cancellationToken);
            var stockGuard = SaleWorkflow.EnsureSufficientStockToIssue(currentStock, item.Quantity);
            if (stockGuard.IsFailure)
            {
                return Result.Failure<SaleDetail>(stockGuard.Error!);
            }
        }

        foreach (var item in sale.Items)
        {
            var removeResult = await _inventoryService.RemoveStockAsync(new RemoveStockRequest(
                item.ProductId, item.Quantity, StockMovementType.SaleIssue, StockReferenceType.Sale, sale.Id,
                $"Sale {sale.SaleNumber} invoiced"), cancellationToken);

            if (removeResult.IsFailure)
            {
                return Result.Failure<SaleDetail>($"Failed to issue stock for one of the items: {removeResult.Error}");
            }
        }

        sale.InvoiceNumber = await GenerateNextNumberAsync("InvoiceNumberTag", SaleWorkflow.FormatInvoiceNumber, cancellationToken);
        sale.Status = SaleStatus.Invoiced;

        await _auditLogger.LogAsync(
            AuditAction.Updated, nameof(Sale), sale.Id,
            $"Sale '{sale.SaleNumber}' invoiced as '{sale.InvoiceNumber}' - stock issued for {sale.Items.Count} item(s).",
            cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToDetailByIdAsync(sale.Id, cancellationToken));
    }

    public async Task<Result<SaleDetail>> CancelAsync(Guid saleId, CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Clear();

        var sale = await _context.Sales.Include(s => s.Items).SingleOrDefaultAsync(s => s.Id == saleId, cancellationToken);
        if (sale is null)
        {
            return Result.Failure<SaleDetail>("Sale not found.");
        }

        var cancelGuard = SaleWorkflow.EnsureCancellable(sale.Status);
        if (cancelGuard.IsFailure)
        {
            return Result.Failure<SaleDetail>(cancelGuard.Error!);
        }

        if (sale.Status == SaleStatus.Invoiced)
        {
            // Restoring stock can never fail on a sufficiency basis - adding
            // back what was issued cannot make anything negative, unlike
            // Purchase's reversal (which removes stock and can fail).
            foreach (var item in sale.Items)
            {
                var addResult = await _inventoryService.AddStockAsync(new AddStockRequest(
                    item.ProductId, item.Quantity, StockMovementType.SalesReturn, StockReferenceType.Sale, sale.Id,
                    $"Sale {sale.SaleNumber} cancelled"), cancellationToken);

                if (addResult.IsFailure)
                {
                    return Result.Failure<SaleDetail>($"Failed to restore stock for one of the items: {addResult.Error}");
                }
            }
        }

        sale.Status = SaleStatus.Cancelled;

        await _auditLogger.LogAsync(
            AuditAction.Updated, nameof(Sale), sale.Id, $"Sale '{sale.SaleNumber}' cancelled.", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToDetailByIdAsync(sale.Id, cancellationToken));
    }

    private async Task<string?> ValidateAsync(
        Guid customerId, IReadOnlyList<SaleItemRequest> items, CancellationToken cancellationToken)
    {
        var hasItemsCheck = SaleWorkflow.ValidateHasItems(items.Count);
        if (hasItemsCheck.IsFailure)
        {
            return hasItemsCheck.Error;
        }

        var customerExists = await _context.Customers.AnyAsync(c => c.Id == customerId, cancellationToken);
        if (!customerExists)
        {
            return "Selected customer does not exist.";
        }

        foreach (var item in items)
        {
            var shapeCheck = SaleWorkflow.ValidateItemShape(item.Quantity, item.UnitPrice, item.DiscountAmount);
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

    /// <summary>
    /// Shared collision-safe numbering helper for both SaleNumber and
    /// InvoiceNumber - same installation-tag scheme as Purchase/Stock
    /// Adjustment, but SaleNumber and InvoiceNumber are separate sequences
    /// (settingKey distinguishes them), each counted against how many
    /// Sales already have a non-null value in the corresponding column.
    /// </summary>
    private async Task<string> GenerateNextNumberAsync(
        string settingKey, Func<string, int, string> formatter, CancellationToken cancellationToken)
    {
        var clientTag = await GetOrCreateClientTagAsync(cancellationToken);

        var count = settingKey == "InvoiceNumberTag"
            ? await _context.Sales.IgnoreQueryFilters().CountAsync(s => s.InvoiceNumber != null, cancellationToken)
            : await _context.Sales.IgnoreQueryFilters().CountAsync(cancellationToken);

        return formatter(clientTag, count + 1);
    }

    /// <summary>Reuses the SAME "ClientInstallationTag" ApplicationSetting Purchase/StockAdjustment already create - one tag per install, shared across every document type.</summary>
    private async Task<string> GetOrCreateClientTagAsync(CancellationToken cancellationToken)
    {
        const string settingKey = "ClientInstallationTag";

        var existing = await _context.ApplicationSettings.SingleOrDefaultAsync(s => s.Key == settingKey, cancellationToken);
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
                    + "(Purchases, Stock Adjustments, Sales/Invoices) so numbers generated on different "
                    + "offline installs can never collide. Generated once, on first use.",
            });
        }

        return tag;
    }

    private async Task<SaleDetail> ToDetailByIdAsync(Guid saleId, CancellationToken cancellationToken)
    {
        var sale = await _context.Sales
            .AsNoTracking()
            .Include(s => s.Customer)
            .Include(s => s.Items)
            .ThenInclude(i => i.Product)
            .SingleAsync(s => s.Id == saleId, cancellationToken);

        return ToDetail(sale);
    }

    private static SaleDetail ToDetail(Sale sale)
    {
        var items = sale.Items
            .Select(i => new SaleItemDetail(i.Id, i.ProductId, i.Product.Sku, i.Product.Name, i.Quantity, i.UnitPrice, i.DiscountAmount, i.LineTotal))
            .ToList();

        return new SaleDetail(
            sale.Id, sale.SaleNumber, sale.InvoiceNumber, sale.CustomerId, sale.Customer.Name, sale.SaleDate, sale.Status,
            sale.Subtotal, sale.DiscountAmount, sale.TaxAmount, sale.TotalAmount, sale.Notes, items);
    }
}
