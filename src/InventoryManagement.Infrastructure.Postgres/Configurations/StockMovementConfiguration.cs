using InventoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventoryManagement.Infrastructure.Postgres.Configurations;

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.Property(m => m.QuantityChange).HasPrecision(18, 4);
        builder.Property(m => m.QuantityBalanceAfter).HasPrecision(18, 4);
        builder.Property(m => m.Notes).HasMaxLength(1000);

        builder.HasOne(m => m.Product)
            .WithMany(p => p.StockMovements)
            .HasForeignKey(m => m.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => new { m.ReferenceType, m.ReferenceId })
            .HasDatabaseName("IX_StockMovements_ReferenceType_ReferenceId");
    }
}
