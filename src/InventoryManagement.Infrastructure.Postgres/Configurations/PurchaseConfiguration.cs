using InventoryManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventoryManagement.Infrastructure.Postgres.Configurations;

public class PurchaseConfiguration : IEntityTypeConfiguration<Purchase>
{
    public void Configure(EntityTypeBuilder<Purchase> builder)
    {
        builder.Property(p => p.PurchaseNumber).IsRequired().HasMaxLength(50);
        builder.Property(p => p.SupplierInvoiceNumber).HasMaxLength(100);
        builder.Property(p => p.Notes).HasMaxLength(2000);

        builder.Property(p => p.Subtotal).HasPrecision(18, 4);
        builder.Property(p => p.TaxAmount).HasPrecision(18, 4);
        builder.Property(p => p.DiscountAmount).HasPrecision(18, 4);
        builder.Property(p => p.TotalAmount).HasPrecision(18, 4);

        builder.HasIndex(p => p.PurchaseNumber).IsUnique();

        builder.HasOne(p => p.Supplier)
            .WithMany(s => s.Purchases)
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        // Real optimistic concurrency, unlike the SQLite side (see
        // BaseEntity's remarks: SQLite has no equivalent to this, which is
        // exactly why BaseEntity deliberately carries no RowVersion
        // property at all - the two providers get different, each
        // provider-appropriate concurrency strategies rather than forcing
        // one design onto both). Postgres's xmin system column changes on
        // every row update automatically with zero extra storage or
        // application code - EF Core just needs to be told to treat it as
        // a concurrency token.
        builder.Property<uint>("xmin")
            .IsRowVersion()
            .HasColumnName("xmin");

        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}
