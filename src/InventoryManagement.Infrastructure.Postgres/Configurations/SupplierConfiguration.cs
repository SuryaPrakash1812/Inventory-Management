using InventoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventoryManagement.Infrastructure.Postgres.Configurations;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        builder.Property(s => s.ContactPerson).HasMaxLength(200);
        builder.Property(s => s.Email).HasMaxLength(300);
        builder.Property(s => s.Phone).HasMaxLength(50);
        builder.Property(s => s.Address).HasMaxLength(500);
        builder.Property(s => s.TaxId).HasMaxLength(100);
        builder.Property(s => s.Notes).HasMaxLength(2000);

        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}
