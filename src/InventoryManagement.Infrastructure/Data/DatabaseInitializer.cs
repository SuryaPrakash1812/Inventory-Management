using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Infrastructure.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InventoryManagement.Infrastructure.Data;

/// <summary>
/// One-time database setup: apply any pending EF Core migrations, then
/// confirm the database is actually reachable. Seed reference data (Roles,
/// Permissions) is applied as part of migrations themselves (via HasData),
/// not here - that keeps it versioned alongside the schema it belongs to.
///
/// NOTE: until an initial migration exists (see Infrastructure/README or the
/// project's setup notes for the exact `dotnet ef migrations add` command),
/// MigrateAsync has nothing to apply and the database will contain no
/// application tables yet - only EF's own migrations-history table.
/// </summary>
public sealed class DatabaseInitializer : IDatabaseInitializer
{
    private readonly InventoryDbContext _context;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(InventoryDbContext context, ILogger<DatabaseInitializer> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(AppPaths.AppDataRoot);

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
                $"Database migration completed but the database at '{AppPaths.DatabaseFilePath}' is not reachable.");
        }

        _logger.LogInformation("Database ready at {Path}", AppPaths.DatabaseFilePath);
    }
}
