namespace InventoryManagement.Sync;

/// <summary>
/// Drains pending Outbox operations to the API when online. Deliberately
/// NOT wired to run automatically (no background timer, no
/// connectivity-restored trigger) as part of this foundation step -
/// something must call SyncPendingOperationsAsync explicitly. Wiring it to
/// run autonomously, plus retry/backoff scheduling and conflict
/// resolution, is "the complete Sync Engine" work named as explicitly
/// out of scope.
/// </summary>
public interface ISyncEngine
{
    Task<SyncRunSummary> SyncPendingOperationsAsync(CancellationToken cancellationToken = default);
}

public sealed record SyncRunSummary(int Attempted, int Succeeded, int Failed);
