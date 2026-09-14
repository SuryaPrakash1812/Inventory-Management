using InventoryManagement.Domain.Common;
using InventoryManagement.Domain.Enums;

namespace InventoryManagement.Domain.Entities;

/// <summary>
/// A purchase order/invoice from a supplier. <see cref="PurchaseNumber"/> is
/// this application's own internal document number; <see cref="SupplierInvoiceNumber"/>
/// is the supplier's own invoice reference, recorded separately so the two
/// can be matched during reconciliation without conflating them.
/// </summary>
public class Purchase : AuditableSoftDeleteEntity
{
    public string PurchaseNumber { get; set; } = string.Empty;

    public string? SupplierInvoiceNumber { get; set; }

    public Guid SupplierId { get; set; }

    public Supplier Supplier { get; set; } = null!;

    public DateTimeOffset PurchaseDate { get; set; }

    public PurchaseStatus Status { get; set; } = PurchaseStatus.Draft;

    public PurchasePaymentStatus PaymentStatus { get; set; } = PurchasePaymentStatus.Unpaid;

    public decimal Subtotal { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal TotalAmount { get; set; }

    public string? Notes { get; set; }

    public ICollection<PurchaseItem> Items { get; set; } = new List<PurchaseItem>();
}
