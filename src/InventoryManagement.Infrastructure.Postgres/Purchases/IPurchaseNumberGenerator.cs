namespace InventoryManagement.Infrastructure.Postgres.Purchases;

/// <summary>
/// Generates the authoritative, server-side PurchaseNumber. Extracted as
/// an interface specifically so PurchaseSyncService's idempotency
/// behavior (the mandatory "duplicate OperationId never creates a second
/// Purchase" scenario) can be tested with a fast, in-memory test double,
/// without needing a real PostgreSQL connection just to exercise
/// nextval() - see PostgresSequencePurchaseNumberGenerator for the real
/// implementation and why raw ADO.NET is used there.
/// </summary>
public interface IPurchaseNumberGenerator
{
    Task<string> GenerateAsync(CancellationToken cancellationToken = default);
}
