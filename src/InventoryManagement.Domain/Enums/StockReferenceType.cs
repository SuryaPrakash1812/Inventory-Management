namespace InventoryManagement.Domain.Enums;

/// <summary>
/// Which kind of document a <see cref="Entities.StockMovement"/> traces back
/// to. Combined with the movement's ReferenceId (a plain Guid, not a real
/// foreign key, since it can point to different tables), this lets the
/// ledger answer "what caused this stock change" without needing a separate
/// nullable FK column per possible source document.
/// </summary>
public enum StockReferenceType
{
    Purchase = 0,
    Sale = 1,
    StockAdjustment = 2,
    Manual = 3,
}
