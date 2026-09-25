using InventoryManagement.Domain.Entities;

namespace InventoryManagement.Sync.Outbox;

public interface IOutboxProcessor
{
    Task<IReadOnlyList<OutboxOperation>> GetPendingOperationsAsync(CancellationToken cancellationToken = default);

    Task MarkSyncedAsync(Guid operationId, CancellationToken cancellationToken = default);

    Task MarkFailedAsync(Guid operationId, string errorMessage, CancellationToken cancellationToken = default);
}
