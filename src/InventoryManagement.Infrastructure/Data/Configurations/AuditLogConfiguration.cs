using InventoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventoryManagement.Infrastructure.Data.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.Property(a => a.EntityName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(a => a.Details).HasMaxLength(4000);

        // The audit trail's main screen is "everything that happened to this
        // record" and "everything that happened recently" - these two
        // indexes cover both without scanning the whole table.
        builder.HasIndex(a => new { a.EntityName, a.EntityId })
            .HasDatabaseName("IX_AuditLogs_EntityName_EntityId");

        builder.HasIndex(a => a.OccurredAtUtc)
            .HasDatabaseName("IX_AuditLogs_OccurredAtUtc");
    }
}
