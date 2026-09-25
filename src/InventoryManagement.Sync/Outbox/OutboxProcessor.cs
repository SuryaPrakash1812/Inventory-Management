using InventoryManagement.Domain.Entities;
using InventoryManagement.Domain.Enums;
using InventoryManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Sync.Outbox;

/// <summary>
/// Every method here opens its own short-lived InventoryDbContext via
/// ISyncDbContextFactory (Decision 10) and disposes it before returning -
/// never the application's shared, long-lived context.
/// </summary>
public sealed class OutboxProcessor : IOutboxProcessor
{
    // Simple exponential backoff: 30s, 60s, 120s, 240s, capped at 30 minutes.
    // "Controlled backoff" per the architecture decision - not a fixed
    // delay (which wastes time on transient blips) and not immediate
    // retry-forever (which hammers a struggling server).
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);

    private readonly ISyncDbContextFactory _dbContextFactory;

    public OutboxProcessor(ISyncDbContextFactory dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<IReadOnlyList<OutboxOperation>> GetPendingOperationsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = _dbContextFactory.CreateDbContext();

        var candidates = await context.OutboxOperations
            .Where(o => o.Status == OutboxOperationStatus.Pending)
            .OrderBy(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;

        // A first attempt (RetryCount == 0, LastAttemptAtUtc == null) is
        // never held back - backoff only applies to operations that have
        // already failed at least once.
        return candidates
            .Where(o => o.LastAttemptAtUtc is not { } lastAttempt || now - lastAttempt >= BackoffFor(o.RetryCount))
            .ToList();
    }

    private static TimeSpan BackoffFor(int retryCount)
    {
        var seconds = 30 * Math.Pow(2, Math.Max(0, retryCount - 1));
        var backoff = TimeSpan.FromSeconds(seconds);
        return backoff > MaxBackoff ? MaxBackoff : backoff;
    }

    public async Task MarkSyncedAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        await using var context = _dbContextFactory.CreateDbContext();

        var operation = await context.OutboxOperations.SingleOrDefaultAsync(o => o.Id == operationId, cancellationToken);
        if (operation is null)
        {
            return;
        }

        operation.Status = OutboxOperationStatus.Synced;
        operation.LastAttemptAtUtc = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(Guid operationId, string errorMessage, CancellationToken cancellationToken = default)
    {
        await using var context = _dbContextFactory.CreateDbContext();

        var operation = await context.OutboxOperations.SingleOrDefaultAsync(o => o.Id == operationId, cancellationToken);
        if (operation is null)
        {
            return;
        }

        // Terminal - stays out of GetPendingOperationsAsync (which only
        // ever returns Pending) forever, so it is never retried. The
        // payload and error message remain on the row for an admin to
        // review, per the architecture decision to never silently discard
        // a permanent failure.
        operation.Status = OutboxOperationStatus.Failed;
        operation.RetryCount += 1;
        operation.LastAttemptAtUtc = DateTimeOffset.UtcNow;
        operation.ErrorMessage = errorMessage;

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkTransientFailureAsync(Guid operationId, string errorMessage, CancellationToken cancellationToken = default)
    {
        await using var context = _dbContextFactory.CreateDbContext();

        var operation = await context.OutboxOperations.SingleOrDefaultAsync(o => o.Id == operationId, cancellationToken);
        if (operation is null)
        {
            return;
        }

        // Deliberately stays/returns to Pending (not a new "Failed" state)
        // so the next sync run's GetPendingOperationsAsync picks it up
        // again automatically once its backoff window has passed - no
        // separate "retry queue" needed.
        operation.Status = OutboxOperationStatus.Pending;
        operation.RetryCount += 1;
        operation.LastAttemptAtUtc = DateTimeOffset.UtcNow;
        operation.ErrorMessage = errorMessage;

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task ApplyServerPurchaseNumberAsync(Guid purchaseId, string serverPurchaseNumber, CancellationToken cancellationToken = default)
    {
        await using var context = _dbContextFactory.CreateDbContext();

        var purchase = await context.Purchases.SingleOrDefaultAsync(p => p.Id == purchaseId, cancellationToken);
        if (purchase is null)
        {
            // The purchase could theoretically have been deleted locally
            // between when it was queued and when sync completed - not
            // treated as an error here, just nothing left to update.
            return;
        }

        purchase.PurchaseNumber = serverPurchaseNumber;

        await context.SaveChangesAsync(cancellationToken);
    }
}
