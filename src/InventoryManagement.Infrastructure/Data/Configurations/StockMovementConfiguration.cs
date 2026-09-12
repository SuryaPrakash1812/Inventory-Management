using InventoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventoryManagement.Infrastructure.Data.Configurations;

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.Property(m => m.QuantityChange).HasPrecision(18, 4);
        builder.Property(m => m.QuantityBalanceAfter).HasPrecision(18, 4);
        builder.Property(m => m.Notes).HasMaxLength(1000);

        // Every history screen for a product reads "movements for this
        // product, newest first" - this composite index is what makes that
        // query fast without a full table scan.
        builder.HasIndex(m => new { m.ProductId, m.OccurredAtUtc })
            .HasDatabaseName("IX_StockMovements_ProductId_OccurredAtUtc");

        // Looking up "what movements did this Purchase/Sale/Adjustment
        // produce" uses this one.
        builder.HasIndex(m => new { m.ReferenceType, m.ReferenceId })
            .HasDatabaseName("IX_StockMovements_ReferenceType_ReferenceId");

        builder.HasOne(m => m.Product)
            .WithMany(p => p.StockMovements)
            .HasForeignKey(m => m.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
