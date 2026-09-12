using InventoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventoryManagement.Infrastructure.Data.Configurations;

public class BackupRecordConfiguration : IEntityTypeConfiguration<BackupRecord>
{
    public void Configure(EntityTypeBuilder<BackupRecord> builder)
    {
        builder.Property(b => b.FileName)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(b => b.FilePath)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(b => b.Notes).HasMaxLength(1000);

        builder.HasIndex(b => b.CreatedAtUtc)
            .HasDatabaseName("IX_BackupRecords_CreatedAtUtc");
    }
}
