using InventoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventoryManagement.Infrastructure.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.Property(p => p.Sku)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(p => p.Barcode)
            .HasMaxLength(64);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(p => p.Description)
            .HasMaxLength(2000);

        builder.Property(p => p.Brand)
            .HasMaxLength(200);

        builder.Property(p => p.Unit)
            .IsRequired()
            .HasMaxLength(20);

        // SQLite has no native decimal type - EF Core stores it as TEXT via a
        // value converter. HasPrecision still matters: it tells that
        // converter how many digits/decimal places to round to consistently,
        // regardless of the storage format.
        builder.Property(p => p.CostPrice).HasPrecision(18, 4);
        builder.Property(p => p.SellingPrice).HasPrecision(18, 4);
        builder.Property(p => p.TaxPercentage).HasPrecision(5, 2);
        builder.Property(p => p.ReorderLevel).HasPrecision(18, 4);
        builder.Property(p => p.QuantityOnHand).HasPrecision(18, 4);

        builder.HasIndex(p => p.Sku)
            .IsUnique()
            .HasDatabaseName("IX_Products_Sku");

        builder.HasIndex(p => p.Barcode)
            .IsUnique()
            .HasDatabaseName("IX_Products_Barcode");

        builder.HasIndex(p => p.Name)
            .HasDatabaseName("IX_Products_Name");

        builder.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.PrimarySupplier)
            .WithMany(s => s.SuppliedProducts)
            .HasForeignKey(p => p.PrimarySupplierId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
