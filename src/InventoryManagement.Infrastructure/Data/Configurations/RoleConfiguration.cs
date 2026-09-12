using InventoryManagement.Domain.Entities;
using InventoryManagement.Infrastructure.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventoryManagement.Infrastructure.Data.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    private static readonly DateTimeOffset SeededAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(r => r.Description).HasMaxLength(300);

        builder.HasIndex(r => r.Name)
            .IsUnique()
            .HasDatabaseName("IX_Roles_Name");

        builder.HasData(
            new Role
            {
                Id = SeedIds.Roles.Administrator,
                Name = "Administrator",
                Description = "Full access to every area of the application.",
                CreatedAtUtc = SeededAt,
            },
            new Role
            {
                Id = SeedIds.Roles.StandardUser,
                Name = "Standard User",
                Description = "Read-only access to products, purchases, sales, and reports.",
                CreatedAtUtc = SeededAt,
            });

        // Role <-> Permission is a plain many-to-many with no extra columns of
        // its own, so EF Core's implicit join table (skip navigation) is used
        // rather than an explicit join entity - there's nothing an explicit
        // join entity would add here beyond ceremony.
        builder.HasMany(r => r.Permissions)
            .WithMany(p => p.Roles)
            .UsingEntity<Dictionary<string, object>>(
                "RolePermissions",
                j => j.HasOne<Permission>().WithMany().HasForeignKey("PermissionId").OnDelete(DeleteBehavior.Cascade),
                j => j.HasOne<Role>().WithMany().HasForeignKey("RoleId").OnDelete(DeleteBehavior.Cascade),
                j =>
                {
                    j.HasKey("RoleId", "PermissionId");
                    j.HasData(
                        // Administrator: every permission.
                        Grant(SeedIds.Roles.Administrator, SeedIds.Permissions.ProductsView),
                        Grant(SeedIds.Roles.Administrator, SeedIds.Permissions.ProductsManage),
                        Grant(SeedIds.Roles.Administrator, SeedIds.Permissions.CategoriesManage),
                        Grant(SeedIds.Roles.Administrator, SeedIds.Permissions.SuppliersManage),
                        Grant(SeedIds.Roles.Administrator, SeedIds.Permissions.CustomersManage),
                        Grant(SeedIds.Roles.Administrator, SeedIds.Permissions.PurchasesView),
                        Grant(SeedIds.Roles.Administrator, SeedIds.Permissions.PurchasesManage),
                        Grant(SeedIds.Roles.Administrator, SeedIds.Permissions.SalesView),
                        Grant(SeedIds.Roles.Administrator, SeedIds.Permissions.SalesManage),
                        Grant(SeedIds.Roles.Administrator, SeedIds.Permissions.InventoryManage),
                        Grant(SeedIds.Roles.Administrator, SeedIds.Permissions.ReportsView),
                        Grant(SeedIds.Roles.Administrator, SeedIds.Permissions.UsersManage),
                        Grant(SeedIds.Roles.Administrator, SeedIds.Permissions.SettingsManage),
                        Grant(SeedIds.Roles.Administrator, SeedIds.Permissions.BackupManage),
                        // Standard User: view-only.
                        Grant(SeedIds.Roles.StandardUser, SeedIds.Permissions.ProductsView),
                        Grant(SeedIds.Roles.StandardUser, SeedIds.Permissions.PurchasesView),
                        Grant(SeedIds.Roles.StandardUser, SeedIds.Permissions.SalesView),
                        Grant(SeedIds.Roles.StandardUser, SeedIds.Permissions.ReportsView));
                });
    }

    private static Dictionary<string, object> Grant(Guid roleId, Guid permissionId) => new()
    {
        ["RoleId"] = roleId,
        ["PermissionId"] = permissionId,
    };
}
