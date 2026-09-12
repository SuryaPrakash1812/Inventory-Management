using InventoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventoryManagement.Infrastructure.Data.Configurations;

public class PurchaseConfiguration : IEntityTypeConfiguration<Purchase>
{
    public void Configure(EntityTypeBuilder<Purchase> builder)
    {
        builder.Property(p => p.PurchaseNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(p => p.SupplierInvoiceNumber)
            .HasMaxLength(50);

        builder.Property(p => p.Notes).HasMaxLength(2000);

        builder.Property(p => p.Subtotal).HasPrecision(18, 4);
        builder.Property(p => p.TaxAmount).HasPrecision(18, 4);
        builder.Property(p => p.DiscountAmount).HasPrecision(18, 4);
        builder.Property(p => p.TotalAmount).HasPrecision(18, 4);

        builder.HasIndex(p => p.PurchaseNumber)
            .IsUnique()
            .HasDatabaseName("IX_Purchases_PurchaseNumber");

        builder.HasIndex(p => p.SupplierInvoiceNumber)
            .HasDatabaseName("IX_Purchases_SupplierInvoiceNumber");

        builder.HasOne(p => p.Supplier)
            .WithMany(s => s.Purchases)
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
