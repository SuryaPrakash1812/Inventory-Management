using InventoryManagement.Domain.Entities;
using InventoryManagement.Infrastructure.Postgres.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Infrastructure.Postgres;

/// <summary>
/// PostgreSQL persistence for the API. Deliberately scoped, for now, to
/// the Purchase module and its direct dependencies (Category, Product,
/// Supplier, StockMovement) plus sync idempotency tracking - not the full
/// entity set InventoryDbContext (SQLite) exposes. Customer, Sale,
/// SaleItem, StockAdjustment, User/Role/Permission, AuditLog and the
/// others are intentionally not yet mapped here; see the migration report
/// for why (this step's job is Purchase end-to-end, not full schema
/// parity) and add them the same way (a DbSet here plus an
/// IEntityTypeConfiguration in Configurations/) when their module is
/// migrated.
///
/// Does NOT reuse InventoryManagement.Infrastructure's configuration
/// classes - those were tuned around SQLite-specific limitations (no
/// native decimal handling quirks the same way, no row-versioning
/// support, DateTimeOffset ordering issues) that do not apply to
/// PostgreSQL, which supports things SQLite does not (real xmin-based
/// optimistic concurrency, proper decimal, proper timestamp comparison/
/// ordering). Same Domain entities, independent configuration and
/// migration history.
/// </summary>
public class InventoryPostgresDbContext : DbContext
{
    public InventoryPostgresDbContext(DbContextOptions<InventoryPostgresDbContext> options)
        : base(options)
    {
    }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<PurchaseItem> PurchaseItems => Set<PurchaseItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<ProcessedOperation> ProcessedOperations => Set<ProcessedOperation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryPostgresDbContext).Assembly);
    }
}
