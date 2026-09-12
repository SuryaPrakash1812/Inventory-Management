using InventoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventoryManagement.Infrastructure.Data.Configurations;

public class ApplicationSettingConfiguration : IEntityTypeConfiguration<ApplicationSetting>
{
    public void Configure(EntityTypeBuilder<ApplicationSetting> builder)
    {
        builder.Property(s => s.Key)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.Value).HasMaxLength(4000);
        builder.Property(s => s.Description).HasMaxLength(500);

        builder.HasIndex(s => s.Key)
            .IsUnique()
            .HasDatabaseName("IX_ApplicationSettings_Key");
    }
}
