using System.Linq.Expressions;
using InventoryManagement.Domain.Common;
using InventoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace InventoryManagement.Infrastructure.Data;

/// <summary>
/// The application's single EF Core DbContext. All entity configuration
/// (keys, indexes, relationships, precision) lives in the
/// <c>IEntityTypeConfiguration&lt;T&gt;</c> classes under Data/Configurations -
/// this class only wires them up and adds the one piece of cross-cutting
/// behavior that can't live in a single entity's configuration: the global
/// soft-delete query filter.
/// </summary>
public class InventoryDbContext : DbContext, IAppDbContext
{
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options)
        : base(options)
    {
    }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<PurchaseItem> PurchaseItems => Set<PurchaseItem>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<StockAdjustmentItem> StockAdjustmentItems => Set<StockAdjustmentItem>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ApplicationSetting> ApplicationSettings => Set<ApplicationSetting>();
    public DbSet<BackupRecord> BackupRecords => Set<BackupRecord>();
    public DbSet<OutboxOperation> OutboxOperations => Set<OutboxOperation>();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        Database.BeginTransactionAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContext).Assembly);

        ApplySoftDeleteQueryFilters(modelBuilder);
    }

    /// <summary>
    /// Every entity deriving from <see cref="AuditableSoftDeleteEntity"/>
    /// automatically gets a "WHERE IsDeleted = 0" filter applied to every
    /// query, so a deleted Product (say) simply stops appearing anywhere
    /// without every single query in the app having to remember to check
    /// IsDeleted itself. Built via reflection once here, rather than
    /// repeating <c>HasQueryFilter(e => !e.IsDeleted)</c> in seventeen
    /// separate configuration classes.
    /// </summary>
    private static void ApplySoftDeleteQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var isDeletedProperty = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
            var notDeleted = Expression.Not(isDeletedProperty);
            var lambda = Expression.Lambda(notDeleted, parameter);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
        }
    }
}
