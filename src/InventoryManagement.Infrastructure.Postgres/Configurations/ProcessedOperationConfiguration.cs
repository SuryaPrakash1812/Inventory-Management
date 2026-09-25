using InventoryManagement.Infrastructure.Postgres.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventoryManagement.Infrastructure.Postgres.Configurations;

public class ProcessedOperationConfiguration : IEntityTypeConfiguration<ProcessedOperation>
{
    public void Configure(EntityTypeBuilder<ProcessedOperation> builder)
    {
        builder.HasKey(o => o.OperationId);

        builder.Property(o => o.OperationType).IsRequired().HasMaxLength(100);
        builder.Property(o => o.EntityType).IsRequired().HasMaxLength(100);
        builder.Property(o => o.ResultJson).IsRequired();
    }
}
