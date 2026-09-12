using InventoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace InventoryManagement.Application.Common.Interfaces;

/// <summary>
/// Everything an Application-layer service needs from the database, without
/// depending on the concrete EF Core DbContext (which lives in Infrastructure).
/// Deliberately not a full generic-repository abstraction over every entity -
/// that would just hide EF Core's own (already good) querying API behind a
/// thinner one. This exists so services can be unit-tested against a fake
/// implementation and so no Application-layer code needs a project reference
/// to Infrastructure.
/// </summary>
public interface IAppDbContext
{
    DbSet<Category> Categories { get; }
    DbSet<Product> Products { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<Customer> Customers { get; }
    DbSet<Purchase> Purchases { get; }
    DbSet<PurchaseItem> PurchaseItems { get; }
    DbSet<Sale> Sales { get; }
    DbSet<SaleItem> SaleItems { get; }
    DbSet<StockMovement> StockMovements { get; }
    DbSet<StockAdjustment> StockAdjustments { get; }
    DbSet<StockAdjustmentItem> StockAdjustmentItems { get; }
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<ApplicationSetting> ApplicationSettings { get; }
    DbSet<BackupRecord> BackupRecords { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts an explicit transaction spanning more than one SaveChangesAsync
    /// call. A single SaveChangesAsync call is already atomic on its own -
    /// this is only for the rarer case of needing to read back generated data
    /// partway through a multi-step, inventory-affecting operation.
    /// </summary>
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
