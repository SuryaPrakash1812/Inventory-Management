namespace InventoryManagement.Application.Common.Interfaces;

/// <summary>
/// Ensures the local database exists and is up to date before the app starts
/// using it. Implemented in Infrastructure (it needs EF Core), called once
/// from the composition root at startup.
/// </summary>
public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
