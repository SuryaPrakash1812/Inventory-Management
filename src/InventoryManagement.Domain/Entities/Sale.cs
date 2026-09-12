using InventoryManagement.Domain.Common;
using InventoryManagement.Domain.Enums;

namespace InventoryManagement.Domain.Entities;

/// <summary>
/// An order/invoice to a customer. <see cref="SaleNumber"/> is assigned when
/// the sale is first created (internal document number); <see cref="InvoiceNumber"/>
/// is assigned separately when the sale is invoiced (see <see cref="SaleStatus.Invoiced"/>),
/// which is why it is nullable - a draft sale has no invoice number yet.
/// </summary>
public class Sale : AuditableSoftDeleteEntity
{
    public string SaleNumber { get; set; } = string.Empty;

    public string? InvoiceNumber { get; set; }

    public Guid CustomerId { get; set; }

    public Customer Customer { get; set; } = null!;

    public DateTimeOffset SaleDate { get; set; }

    public SaleStatus Status { get; set; } = SaleStatus.Draft;

    public decimal Subtotal { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal TotalAmount { get; set; }

    public string? Notes { get; set; }

    public ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();
}
