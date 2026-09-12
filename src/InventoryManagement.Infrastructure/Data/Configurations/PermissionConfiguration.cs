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
            Seed(SeedIds.Permissions.ProductsView, "Products.View", "View products and categories."),
            Seed(SeedIds.Permissions.ProductsManage, "Products.Manage", "Create, edit, and deactivate products."),
            Seed(SeedIds.Permissions.CategoriesManage, "Categories.Manage", "Create and edit product categories."),
            Seed(SeedIds.Permissions.SuppliersManage, "Suppliers.Manage", "Create and edit suppliers."),
            Seed(SeedIds.Permissions.CustomersManage, "Customers.Manage", "Create and edit customers."),
            Seed(SeedIds.Permissions.PurchasesView, "Purchases.View", "View purchase orders."),
            Seed(SeedIds.Permissions.PurchasesManage, "Purchases.Manage", "Create and confirm purchase orders."),
            Seed(SeedIds.Permissions.SalesView, "Sales.View", "View sales orders/invoices."),
            Seed(SeedIds.Permissions.SalesManage, "Sales.Manage", "Create and invoice sales."),
            Seed(SeedIds.Permissions.InventoryManage, "Inventory.Manage", "Perform manual stock adjustments."),
            Seed(SeedIds.Permissions.ReportsView, "Reports.View", "View dashboards and reports."),
            Seed(SeedIds.Permissions.UsersManage, "Users.Manage", "Manage user accounts and roles."),
            Seed(SeedIds.Permissions.SettingsManage, "Settings.Manage", "Change application-wide settings."),
            Seed(SeedIds.Permissions.BackupManage, "Backup.Manage", "Configure and run backups/restores."));
    }

    private static Permission Seed(Guid id, string name, string description) => new()
    {
        Id = id,
        Name = name,
        Description = description,
        CreatedAtUtc = SeededAt,
    };
}
