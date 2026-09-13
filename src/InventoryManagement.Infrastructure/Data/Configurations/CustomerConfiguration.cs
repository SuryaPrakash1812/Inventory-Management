using InventoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventoryManagement.Infrastructure.Data.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(c => c.ContactPerson).HasMaxLength(200);
        builder.Property(c => c.Email).HasMaxLength(200);
        builder.Property(c => c.Phone).HasMaxLength(50);
        builder.Property(c => c.Address).HasMaxLength(500);
        builder.Property(c => c.TaxId).HasMaxLength(50);
        builder.Property(c => c.Notes).HasMaxLength(2000);

        builder.HasIndex(c => c.Name)
            .HasDatabaseName("IX_Customers_Name");
    }
}
