using InventoryManagement.Domain.Enums;

namespace InventoryManagement.Contracts.Purchases;

public sealed record PurchaseItemContract(
    Guid ProductId,
    decimal Quantity,
    decimal UnitCost,
    decimal DiscountAmount,
    decimal TaxPercentage);

/// <summary>
/// What a client sends to create a Purchase server-side during sync.
/// PurchaseId is the client-generated GUID (Purchase.Id, assigned locally
/// at construction) - the server uses this same Id as authoritative too,
/// since GUIDs are already globally safe for offline creation (Decision
/// 3). PurchaseNumber is deliberately NOT included: the server may assign/
/// record its own authoritative number independent of whatever local
/// display number the client used offline - see the migration report for
/// the current status of that specific piece (interim client-side scheme,
/// server-assignment deferred until the Sync Engine is complete).
/// </summary>
public sealed record CreatePurchaseContract(
    Guid PurchaseId,
    Guid SupplierId,
    string? SupplierInvoiceNumber,
    DateTimeOffset PurchaseDate,
    string? Notes,
    IReadOnlyList<PurchaseItemContract> Items);

public sealed record ConfirmPurchaseContract(Guid PurchaseId);

public sealed record CancelPurchaseContract(Guid PurchaseId);

public sealed record PurchaseSyncResultContract(
    Guid PurchaseId,
    string ServerPurchaseNumber,
    PurchaseStatus Status);
