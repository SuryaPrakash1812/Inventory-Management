using InventoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventoryManagement.Infrastructure.Data.Configurations;

public class OutboxOperationConfiguration : IEntityTypeConfiguration<OutboxOperation>
{
    public void Configure(EntityTypeBuilder<OutboxOperation> builder)
    {
        builder.Property(o => o.OperationType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(o => o.EntityType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(o => o.PayloadJson)
            .IsRequired();

        builder.Property(o => o.ErrorMessage)
            .HasMaxLength(2000);

        // The Sync Engine's primary query is "find Pending operations,
        // oldest first" - this index serves that directly. Status is a
        // small enum with few distinct values, so this index is far more
        // selective in practice than it looks (most rows will be Synced
        // and rarely queried again once processed).
        builder.HasIndex(o => new { o.Status, o.CreatedAtUtc })
            .HasDatabaseName("IX_OutboxOperations_Status_CreatedAtUtc");

        builder.HasIndex(o => new { o.EntityType, o.EntityId })
            .HasDatabaseName("IX_OutboxOperations_EntityType_EntityId");
    }
}
