using InventoryManagement.Application.Auth;
using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Application.Inventory;
using InventoryManagement.Core.Common;
using InventoryManagement.Domain.Entities;
using InventoryManagement.Domain.Enums;
using InventoryManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Infrastructure.Inventory;

public sealed class InventoryService : IInventoryService
{
    private static readonly StockMovementType[] IncreaseMovementTypes =
    {
        StockMovementType.OpeningBalance, StockMovementType.PurchaseReceipt, StockMovementType.SalesReturn,
    };

    private static readonly StockMovementType[] DecreaseMovementTypes =
    {
        StockMovementType.SaleIssue, StockMovementType.PurchaseReturn,
    };

    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ICurrentUserSession _session;

    public InventoryService(IAppDbContext context, IDateTimeProvider dateTimeProvider, ICurrentUserSession session)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
        _session = session;
    }

    public async Task<decimal> GetCurrentStockAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        return await _context.Products
            .Where(p => p.Id == productId)
            .Select(p => p.QuantityOnHand)
            .SingleAsync(cancellationToken);
    }

    public async Task<Result<StockMovementResult>> AddStockAsync(
        AddStockRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0)
        {
            return Result.Failure<StockMovementResult>("Quantity to add must be greater than zero.");
        }

        if (!IncreaseMovementTypes.Contains(request.MovementType))
        {
            return Result.Failure<StockMovementResult>(
                $"{request.MovementType} is not a valid movement type for adding stock.");
        }

        return await ApplyMovementAsync(
            request.ProductId,
            request.Quantity,
            request.MovementType,
            request.ReferenceType,
            request.ReferenceId,
            request.Notes,
            allowNegativeStock: true, // an increase can never drive stock negative
            cancellationToken);
    }

    public async Task<Result<StockMovementResult>> RemoveStockAsync(
        RemoveStockRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0)
        {
            return Result.Failure<StockMovementResult>("Quantity to remove must be greater than zero.");
        }

        if (!DecreaseMovementTypes.Contains(request.MovementType))
        {
            return Result.Failure<StockMovementResult>(
                $"{request.MovementType} is not a valid movement type for removing stock.");
        }

        return await ApplyMovementAsync(
            request.ProductId,
            -request.Quantity,
            request.MovementType,
            request.ReferenceType,
            request.ReferenceId,
            request.Notes,
            request.AllowNegativeStock,
            cancellationToken);
    }

    public async Task<Result<StockMovementResult>> AdjustStockAsync(
        AdjustStockRequest request, CancellationToken cancellationToken = default)
    {
        if (request.QuantityChange == 0)
        {
            return Result.Failure<StockMovementResult>("Adjustment quantity cannot be zero.");
        }

        var movementType = request.QuantityChange > 0
            ? StockMovementType.AdjustmentIncrease
            : StockMovementType.AdjustmentDecrease;

        return await ApplyMovementAsync(
            request.ProductId,
            request.QuantityChange,
            movementType,
            request.ReferenceType,
            request.ReferenceId,
            request.Notes,
            request.AllowNegativeStock,
            cancellationToken);
    }

    public async Task<PagedResult<StockHistoryEntry>> GetStockHistoryAsync(
        StockHistoryQuery query, CancellationToken cancellationToken = default)
    {
        var movements = _context.StockMovements.AsQueryable();

        if (query.ProductId is { } productId)
        {
            movements = movements.Where(m => m.ProductId == productId);
        }

        var totalCount = await movements.CountAsync(cancellationToken);

        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = query.PageSize <= 0 ? 25 : query.PageSize;

        // Same SQLite DateTimeOffset ORDER BY limitation as everywhere else
        // in this codebase (see ProductService.ApplySort remarks) - sorted
        // in memory rather than translated to SQL.
        var all = await movements
            .Select(m => new StockHistoryEntry(
                m.Id, m.ProductId, m.Product.Sku, m.Product.Name, m.MovementType, m.QuantityChange,
                m.QuantityBalanceAfter, m.ReferenceType, m.ReferenceId, m.OccurredAtUtc, m.PerformedByUserId, m.Notes))
            .ToListAsync(cancellationToken);

        var items = all
            .OrderByDescending(e => e.OccurredAtUtc)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new PagedResult<StockHistoryEntry>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<IReadOnlyList<LowStockProduct>> GetLowStockProductsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.Products
            .Where(p => p.IsActive && p.QuantityOnHand <= p.ReorderLevel)
            .OrderBy(p => p.Name)
            .Select(p => new LowStockProduct(p.Id, p.Sku, p.Name, p.QuantityOnHand, p.ReorderLevel))
            .ToListAsync(cancellationToken);
    }

    public async Task<StockValuationResult> GetStockValuationAsync(CancellationToken cancellationToken = default)
    {
        var products = await _context.Products
            .Where(p => p.IsActive)
            .Select(p => new { p.QuantityOnHand, p.CostPrice, p.SellingPrice })
            .ToListAsync(cancellationToken);

        return new StockValuationResult(
            ProductCount: products.Count,
            TotalCostValue: products.Sum(p => p.QuantityOnHand * p.CostPrice),
            TotalRetailValue: products.Sum(p => p.QuantityOnHand * p.SellingPrice));
    }

    /// <summary>
    /// The one place every stock-affecting operation actually happens.
    ///
    /// CONCURRENCY: SQLite allows only one writer across the entire
    /// database file at a time (no per-row locking, unlike SQL Server or
    /// Postgres - a real, documented limitation of the engine, not
    /// something this code can work around). A transaction opened with EF
    /// Core's default BeginTransactionAsync is DEFERRED: it does not
    /// actually acquire SQLite's write lock until the first real write
    /// statement executes. If we read the product's current stock first
    /// and only wrote afterwards, a second concurrent call could read the
    /// same starting balance before either of us writes, and one update
    /// would silently overwrite the other (a classic lost-update race).
    ///
    /// To prevent that, this method forces the tracked Product entity into
    /// EntityState.Modified and calls SaveChangesAsync BEFORE reading its
    /// stock value - even though nothing has changed yet, this issues a
    /// genuine UPDATE statement, which makes SQLite acquire the write lock
    /// immediately. Any other concurrent call attempting the same thing
    /// blocks (and retries, thanks to the busy_timeout pragma - see
    /// SqliteConnectionInterceptor) until we commit or roll back, so by the
    /// time we read QuantityOnHand, we are guaranteed no one else can
    /// change it out from under us. This is coarser than true row-level
    /// locking (it serializes ALL concurrent stock changes against each
    /// other, not just ones touching the same product) - that is the "as
    /// far as SQLite permits" tradeoff inherent to this database engine.
    /// </summary>
    private async Task<Result<StockMovementResult>> ApplyMovementAsync(
        Guid productId,
        decimal signedQuantityChange,
        StockMovementType movementType,
        StockReferenceType referenceType,
        Guid referenceId,
        string? notes,
        bool allowNegativeStock,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.BeginTransactionAsync(cancellationToken);

        var product = await _context.Products.SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure<StockMovementResult>("Product not found.");
        }

        // Force the write lock now - see method remarks above.
        _context.Entry(product).State = EntityState.Modified;
        await _context.SaveChangesAsync(cancellationToken);

        var newBalance = product.QuantityOnHand + signedQuantityChange;

        if (newBalance < 0 && !allowNegativeStock)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure<StockMovementResult>(
                $"Insufficient stock: only {product.QuantityOnHand} available, cannot reduce by {-signedQuantityChange}.");
        }

        var now = _dateTimeProvider.UtcNow;

        var movement = new StockMovement
        {
            ProductId = productId,
            MovementType = movementType,
            QuantityChange = signedQuantityChange,
            QuantityBalanceAfter = newBalance,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            OccurredAtUtc = now,
            PerformedByUserId = _session.CurrentUser?.Id,
            Notes = notes,
        };

        _context.StockMovements.Add(movement);
        product.QuantityOnHand = newBalance;

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Success(new StockMovementResult(
            movement.Id, productId, signedQuantityChange, newBalance, now));
    }
}
