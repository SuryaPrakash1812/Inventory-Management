using InventoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventoryManagement.Infrastructure.Data.Configurations;

public class StockAdjustmentConfiguration : IEntityTypeConfiguration<StockAdjustment>
{
    public void Configure(EntityTypeBuilder<StockAdjustment> builder)
    {
        builder.Property(a => a.AdjustmentNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(a => a.Notes).HasMaxLength(2000);

        builder.HasIndex(a => a.AdjustmentNumber)
            .IsUnique()
            .HasDatabaseName("IX_StockAdjustments_AdjustmentNumber");
    }
}
