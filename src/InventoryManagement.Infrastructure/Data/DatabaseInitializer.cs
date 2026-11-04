using InventoryManagement.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InventoryManagement.Infrastructure.Data;

/// <summary>
/// One-time database setup: apply any pending EF Core migrations, then
/// confirm the database is actually reachable. Seed reference data (Roles,
/// Permissions) is applied as part of migrations themselves (via HasData),
/// not here - that keeps it versioned alongside the schema it belongs to.
///
/// By the time this runs, LocalDatabaseBootstrapper has already ensured the
/// file (if any) is encrypted with this PC's key, so a brand-new database
/// created by MigrateAsync here is encrypted from its very first page.
/// </summary>
public sealed class DatabaseInitializer : IDatabaseInitializer
{
    private readonly InventoryDbContext _context;
    private readonly LocalDatabaseOptions _database;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(InventoryDbContext context, LocalDatabaseOptions database, ILogger<DatabaseInitializer> logger)
    {
        _context = context;
        _database = database;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_database.DatabasePath))!);

        var pendingMigrations = (await _context.Database
            .GetPendingMigrationsAsync(cancellationToken)).ToList();

        if (pendingMigrations.Count > 0)
        {
            _logger.LogInformation(
                "Applying {Count} pending database migration(s): {Migrations}",
                pendingMigrations.Count,
                string.Join(", ", pendingMigrations));
        }

        await _context.Database.MigrateAsync(cancellationToken);

        if (!await _context.Database.CanConnectAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                $"Database migration completed but the database at '{_database.DatabasePath}' is not reachable.");
        }

        _logger.LogInformation("Database ready at {Path}", _database.DatabasePath);
    }
}
