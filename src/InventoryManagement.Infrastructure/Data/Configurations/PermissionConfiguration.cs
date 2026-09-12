using InventoryManagement.Domain.Entities;
using InventoryManagement.Infrastructure.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventoryManagement.Infrastructure.Data.Configurations;

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    /// <summary>Fixed timestamp used for every seeded row - see <see cref="SeedIds"/> remarks on why these must stay stable.</summary>
    private static readonly DateTimeOffset SeededAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.Description).HasMaxLength(300);

        builder.HasIndex(p => p.Name)
            .IsUnique()
            .HasDatabaseName("IX_Permissions_Name");

        builder.HasData(
            Seed(SeedIds.Permissions.DashboardView, "Dashboard.View", "View the dashboard."),
            Seed(SeedIds.Permissions.ProductsView, "Products.View", "View products."),
            Seed(SeedIds.Permissions.ProductsCreate, "Products.Create", "Create new products."),
            Seed(SeedIds.Permissions.ProductsEdit, "Products.Edit", "Edit existing products."),
            Seed(SeedIds.Permissions.ProductsDelete, "Products.Delete", "Delete (deactivate) products."),
            Seed(SeedIds.Permissions.PurchasesView, "Purchases.View", "View purchase orders."),
            Seed(SeedIds.Permissions.PurchasesCreate, "Purchases.Create", "Create purchase orders."),
            Seed(SeedIds.Permissions.PurchasesEdit, "Purchases.Edit", "Edit purchase orders."),
            Seed(SeedIds.Permissions.SalesView, "Sales.View", "View sales orders/invoices."),
            Seed(SeedIds.Permissions.SalesCreate, "Sales.Create", "Create sales orders/invoices."),
            Seed(SeedIds.Permissions.SalesEdit, "Sales.Edit", "Edit sales orders/invoices."),
            Seed(SeedIds.Permissions.InventoryView, "Inventory.View", "View current stock levels."),
            Seed(SeedIds.Permissions.InventoryAdjust, "Inventory.Adjust", "Perform manual stock adjustments."),
            Seed(SeedIds.Permissions.ReportsView, "Reports.View", "View dashboards and reports."),
            Seed(SeedIds.Permissions.UsersView, "Users.View", "View user accounts."),
            Seed(SeedIds.Permissions.UsersManage, "Users.Manage", "Create/edit user accounts and roles."),
            Seed(SeedIds.Permissions.BackupCreate, "Backup.Create", "Create backups."),
            Seed(SeedIds.Permissions.BackupRestore, "Backup.Restore", "Restore from a backup."),
            Seed(SeedIds.Permissions.SettingsManage, "Settings.Manage", "Change application-wide settings."));
    }

    private static Permission Seed(Guid id, string name, string description) => new()
    {
        Id = id,
        Name = name,
        Description = description,
        CreatedAtUtc = SeededAt,
    };
}
