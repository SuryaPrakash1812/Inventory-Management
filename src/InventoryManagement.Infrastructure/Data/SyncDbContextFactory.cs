using InventoryManagement.Infrastructure.Common;
using InventoryManagement.Infrastructure.Data.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Infrastructure.Data;

/// <summary>
/// Decision 10: the Sync Engine must never share the application's
/// long-lived, scoped InventoryDbContext (see App.xaml.cs's _appScope
/// remarks on why that single instance stays open for the whole WPF
/// session). Background synchronization needs its own short-lived
/// context per unit of work - created immediately before use and disposed
/// immediately after - to avoid stale tracking state and concurrent use
/// of a context that is not thread-safe.
///
/// Deliberately NOT registered via EF Core's AddDbContextFactory helper
/// alongside the existing AddDbContext&lt;InventoryDbContext&gt;
/// registration for the same type - AddDbContextFactory also registers a
/// SCOPED InventoryDbContext service of its own, which would conflict
/// with (or silently shadow) the app's existing, carefully-configured
/// registration for the very same type. This is a small, explicit factory
/// instead: it shares the same SQLite connection string and interceptor
/// configuration, but has zero registration overlap with the existing
/// setup, so the rest of the application's DbContext behavior is
/// completely unaffected by this addition.
/// </summary>
public interface ISyncDbContextFactory
{
    InventoryDbContext CreateDbContext();
}

public sealed class SyncDbContextFactory : ISyncDbContextFactory
{
    private readonly AuditableEntitySaveChangesInterceptor _auditInterceptor;
    private readonly SqliteConnectionInterceptor _connectionInterceptor;

    public SyncDbContextFactory(
        AuditableEntitySaveChangesInterceptor auditInterceptor,
        SqliteConnectionInterceptor connectionInterceptor)
    {
        _auditInterceptor = auditInterceptor;
        _connectionInterceptor = connectionInterceptor;
    }

    public InventoryDbContext CreateDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<InventoryDbContext>();
        optionsBuilder.UseSqlite($"Data Source={AppPaths.DatabaseFilePath}");
        optionsBuilder.AddInterceptors(_auditInterceptor, _connectionInterceptor);

        return new InventoryDbContext(optionsBuilder.Options);
    }
}
