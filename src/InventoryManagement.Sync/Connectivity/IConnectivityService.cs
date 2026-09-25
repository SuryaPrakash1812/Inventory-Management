namespace InventoryManagement.Sync.Connectivity;

/// <summary>
/// Centralized decision of whether online operations are currently
/// possible, per the architecture decision that the UI (and every
/// service) should consult this rather than deciding for itself. Backed
/// by a cached boolean so routing decisions (see the migration report's
/// Purchase migration strategy) never need a network round-trip just to
/// decide where to send a request.
/// </summary>
public interface IConnectivityService
{
    bool IsOnline { get; }

    event EventHandler<bool>? ConnectivityChanged;

    /// <summary>Forces an immediate check rather than waiting for the next periodic check, updating IsOnline and raising ConnectivityChanged if it changed.</summary>
    Task<bool> CheckNowAsync(CancellationToken cancellationToken = default);
}
