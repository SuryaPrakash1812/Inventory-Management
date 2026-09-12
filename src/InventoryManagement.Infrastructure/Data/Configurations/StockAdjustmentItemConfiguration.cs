using InventoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventoryManagement.Infrastructure.Data.Configurations;

public class StockAdjustmentItemConfiguration : IEntityTypeConfiguration<StockAdjustmentItem>
{
    public void Configure(EntityTypeBuilder<StockAdjustmentItem> builder)
    {
        builder.Property(i => i.QuantityBefore).HasPrecision(18, 4);
        builder.Property(i => i.QuantityAfter).HasPrecision(18, 4);
        builder.Property(i => i.QuantityChange).HasPrecision(18, 4);
        builder.Property(i => i.Notes).HasMaxLength(500);

        builder.HasOne(i => i.StockAdjustment)
            .WithMany(a => a.Items)
            .HasForeignKey(i => i.StockAdjustmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Product)
            .WithMany(p => p.StockAdjustmentItems)
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
