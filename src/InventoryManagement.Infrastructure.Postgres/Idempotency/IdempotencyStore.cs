using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Infrastructure.Postgres.Idempotency;

public sealed class IdempotencyStore : IIdempotencyStore
{
    private readonly InventoryPostgresDbContext _context;

    public IdempotencyStore(InventoryPostgresDbContext context)
    {
        _context = context;
    }

    public Task<ProcessedOperation?> FindAsync(Guid operationId, CancellationToken cancellationToken = default) =>
        _context.ProcessedOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(o => o.OperationId == operationId, cancellationToken);

    public async Task RecordAsync(
        Guid operationId,
        string operationType,
        string entityType,
        Guid entityId,
        string resultJson,
        DateTimeOffset processedAtUtc,
        CancellationToken cancellationToken = default)
    {
        _context.ProcessedOperations.Add(new ProcessedOperation
        {
            OperationId = operationId,
            OperationType = operationType,
            EntityType = entityType,
            EntityId = entityId,
            ResultJson = resultJson,
            ProcessedAtUtc = processedAtUtc,
        });

        // Deliberately NOT calling SaveChangesAsync here - the caller
        // (e.g. a future PurchaseSyncService) adds the business data changes
        // to the SAME context and commits everything - the ProcessedOperation
        // record and the business data it represents - in one atomic
        // SaveChangesAsync call, mirroring the same "outbox + business data,
        // one transaction" principle used on the client side (Decision 8).
    }
}
