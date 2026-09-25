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
    private readonly ISyncDbContextFactory _dbContextFactory;

    public OutboxProcessor(ISyncDbContextFactory dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<IReadOnlyList<OutboxOperation>> GetPendingOperationsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = _dbContextFactory.CreateDbContext();

        return await context.OutboxOperations
            .Where(o => o.Status == OutboxOperationStatus.Pending)
            .OrderBy(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken);
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

        operation.Status = OutboxOperationStatus.Failed;
        operation.RetryCount += 1;
        operation.LastAttemptAtUtc = DateTimeOffset.UtcNow;
        operation.ErrorMessage = errorMessage;

        await context.SaveChangesAsync(cancellationToken);
    }
}
