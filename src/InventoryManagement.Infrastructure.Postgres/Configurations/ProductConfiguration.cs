using InventoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventoryManagement.Infrastructure.Postgres.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.Property(p => p.Sku).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Barcode).HasMaxLength(100);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(300);
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.Brand).HasMaxLength(200);
        builder.Property(p => p.Unit).IsRequired().HasMaxLength(20);

        // Postgres's native `numeric` type has no scale/precision surprises
        // the way SQLite's TEXT-affinity storage did (see
        // InventoryManagement.Infrastructure's remarks on that) - explicit
        // precision here is a deliberate choice, not a workaround.
        builder.Property(p => p.CostPrice).HasPrecision(18, 4);
        builder.Property(p => p.SellingPrice).HasPrecision(18, 4);
        builder.Property(p => p.TaxPercentage).HasPrecision(5, 2);
        builder.Property(p => p.ReorderLevel).HasPrecision(18, 4);
        builder.Property(p => p.QuantityOnHand).HasPrecision(18, 4);

        builder.HasIndex(p => p.Sku).IsUnique();
        builder.HasIndex(p => p.Barcode).IsUnique().HasFilter("\"Barcode\" IS NOT NULL");

        builder.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.PrimarySupplier)
            .WithMany(s => s.SuppliedProducts)
            .HasForeignKey(p => p.PrimarySupplierId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}
