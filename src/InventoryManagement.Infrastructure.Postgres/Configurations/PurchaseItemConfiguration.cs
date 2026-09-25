using InventoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventoryManagement.Infrastructure.Postgres.Configurations;

public class PurchaseItemConfiguration : IEntityTypeConfiguration<PurchaseItem>
{
    public void Configure(EntityTypeBuilder<PurchaseItem> builder)
    {
        builder.Property(i => i.Quantity).HasPrecision(18, 4);
        builder.Property(i => i.UnitCost).HasPrecision(18, 4);
        builder.Property(i => i.DiscountAmount).HasPrecision(18, 4);
        builder.Property(i => i.TaxPercentage).HasPrecision(5, 2);
        builder.Property(i => i.TaxAmount).HasPrecision(18, 4);
        builder.Property(i => i.LineTotal).HasPrecision(18, 4);

        builder.HasOne(i => i.Purchase)
            .WithMany(p => p.Items)
            .HasForeignKey(i => i.PurchaseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Product)
            .WithMany(p => p.PurchaseItems)
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
