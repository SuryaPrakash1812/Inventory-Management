using InventoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventoryManagement.Infrastructure.Data.Configurations;

public class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.Property(s => s.SaleNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(s => s.InvoiceNumber)
            .HasMaxLength(50);

        builder.Property(s => s.Notes).HasMaxLength(2000);

        builder.Property(s => s.Subtotal).HasPrecision(18, 4);
        builder.Property(s => s.TaxAmount).HasPrecision(18, 4);
        builder.Property(s => s.DiscountAmount).HasPrecision(18, 4);
        builder.Property(s => s.TotalAmount).HasPrecision(18, 4);

        builder.HasIndex(s => s.SaleNumber)
            .IsUnique()
            .HasDatabaseName("IX_Sales_SaleNumber");

        builder.HasIndex(s => s.InvoiceNumber)
            .IsUnique()
            .HasDatabaseName("IX_Sales_InvoiceNumber");

        builder.HasOne(s => s.Customer)
            .WithMany(c => c.Sales)
            .HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
