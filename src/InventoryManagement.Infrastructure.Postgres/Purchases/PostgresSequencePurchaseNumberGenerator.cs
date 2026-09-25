using InventoryManagement.Domain.Purchases;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Infrastructure.Postgres.Purchases;

/// <summary>
/// CRITICAL FIX target: uses a real PostgreSQL SEQUENCE
/// (purchase_number_seq, declared in
/// InventoryPostgresDbContext.OnModelCreating) via nextval(), which is
/// atomic at the database level - two concurrent callers are guaranteed
/// distinct values, unlike the COUNT(*) + 1 scheme this replaces (which
/// had a genuine race window between two simultaneous requests).
///
/// Raw ADO.NET rather than EF Core's SqlQuery&lt;T&gt; helper, to avoid
/// depending on that API's specific column-naming requirements across EF
/// Core versions - nextval() is a single scalar value with no mapping
/// ambiguity either way.
/// </summary>
public sealed class PostgresSequencePurchaseNumberGenerator : IPurchaseNumberGenerator
{
    private readonly InventoryPostgresDbContext _context;

    public PostgresSequencePurchaseNumberGenerator(InventoryPostgresDbContext context)
    {
        _context = context;
    }

    public async Task<string> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var connection = _context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT nextval('purchase_number_seq')";

        var result = await command.ExecuteScalarAsync(cancellationToken);
        var nextValue = Convert.ToInt64(result);

        return PurchaseWorkflow.FormatServerPurchaseNumber(DateTimeOffset.UtcNow.Year, nextValue);
    }
}
