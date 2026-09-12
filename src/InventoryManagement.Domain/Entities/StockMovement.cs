using InventoryManagement.Domain.Common;
using InventoryManagement.Domain.Enums;

namespace InventoryManagement.Domain.Entities;

/// <summary>
/// One entry in the append-only stock ledger: every single change to a
/// product's quantity, ever, for any reason. Rows here are never edited or
/// deleted (a mistake is corrected with an offsetting entry, not a rewrite),
/// which is why this extends <see cref="BaseEntity"/> directly rather than
/// the soft-deletable audit base - there's nothing to soft-delete or update.
///
/// <see cref="ReferenceType"/>/<see cref="ReferenceId"/> together point back
/// at whatever document caused the change (a Purchase, Sale, or
/// StockAdjustment) without a dedicated nullable FK per possible source.
/// </summary>
public class StockMovement : BaseEntity
{
    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public StockMovementType MovementType { get; set; }

    /// <summary>Signed change applied to the product's quantity (positive = stock in, negative = stock out).</summary>
    public decimal QuantityChange { get; set; }

    /// <summary>The product's quantity-on-hand immediately after this movement, cached for fast history display.</summary>
    public decimal QuantityBalanceAfter { get; set; }

    public StockReferenceType ReferenceType { get; set; }

    /// <summary>Id of the Purchase/Sale/StockAdjustment that caused this movement. Not a database FK - see class remarks.</summary>
    public Guid ReferenceId { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }

    public Guid? PerformedByUserId { get; set; }

    public string? Notes { get; set; }
}
