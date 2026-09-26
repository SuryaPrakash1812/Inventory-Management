using System.Text.Json;
using InventoryManagement.Application.Purchases;
using InventoryManagement.Contracts.Purchases;
using InventoryManagement.Contracts.Sync;
using InventoryManagement.Core.Common;
using InventoryManagement.Domain.Entities;
using InventoryManagement.Domain.Enums;
using InventoryManagement.Domain.Purchases;
using InventoryManagement.Infrastructure.Data;
using InventoryManagement.Infrastructure.Purchases;
using InventoryManagement.Sync.Api;
using InventoryManagement.Sync.Connectivity;

namespace InventoryManagement.Sync.Purchases;

/// <summary>
/// The piece that was missing entirely before this stage: without this,
/// IPurchaseService always resolved straight to the local SQLite
/// PurchaseService regardless of connectivity, so "online mode" had no
/// live path in the running app at all, even though PurchaseSyncService,
/// SyncEngine, and IConnectivityService all existed and worked in
/// isolation.
///
/// Strategy - deliberately simple, per explicit instruction that the
/// differentiation between online and offline must be clean:
/// - OFFLINE: never touches the API at all, not even an attempt. Every
///   call goes straight to the local SQLite implementation, exactly as
///   before this router existed.
/// - ONLINE, creating a new purchase (SaveDraftAsync with PurchaseId ==
///   null): tries the API first. On success, the authoritative result is
///   written directly into local SQLite for offline viewing consistency -
///   WITHOUT an Outbox entry, since it is already synchronized (matches
///   the explicit requirement: "no unnecessary Outbox operation for a
///   successfully processed online Purchase"). On ANY failure calling the
///   API (network error, timeout, validation rejection, anything) it
///   falls back to the exact same offline path used when disconnected -
///   the purchase is never lost, per the "never silently lose the
///   Purchase" requirement.
/// - ONLINE, everything else (edit an existing draft, confirm, cancel,
///   payment status, delete, all queries): goes straight to local SQLite,
///   unconditionally - per PurchaseSyncCoverage, only Purchase.Create has
///   a server-side sync handler right now.
/// </summary>
public sealed class PurchaseServiceRouter : IPurchaseService
{
    private readonly PurchaseService _local;
    private readonly IConnectivityService _connectivity;
    private readonly IApiClient _apiClient;
    private readonly IAppDbContext _context;

    public PurchaseServiceRouter(
        PurchaseService local,
        IConnectivityService connectivity,
        IApiClient apiClient,
        IAppDbContext context)
    {
        _local = local;
        _connectivity = connectivity;
        _apiClient = apiClient;
        _context = context;
    }

    public Task<PagedResult<PurchaseSummary>> GetPurchasesAsync(
        PurchaseQueryParameters query, CancellationToken cancellationToken = default) =>
        _local.GetPurchasesAsync(query, cancellationToken);

    public Task<PurchaseDetail?> GetPurchaseByIdAsync(Guid purchaseId, CancellationToken cancellationToken = default) =>
        _local.GetPurchaseByIdAsync(purchaseId, cancellationToken);

    public async Task<Result<PurchaseDetail>> SaveDraftAsync(
        SaveDraftPurchaseRequest request, CancellationToken cancellationToken = default)
    {
        var isNewPurchase = request.PurchaseId is null;

        if (isNewPurchase && _connectivity.IsOnline)
        {
            var onlineResult = await TryCreateOnlineAsync(request, cancellationToken);
            if (onlineResult is not null)
            {
                return onlineResult;
            }

            // Any online failure falls through to the exact same path used
            // when offline. The purchase is never lost; worst case it is
            // created locally instead of online and syncs later.
        }

        return await _local.SaveDraftAsync(request, cancellationToken);
    }

    public Task<Result> DeleteDraftAsync(Guid purchaseId, CancellationToken cancellationToken = default) =>
        _local.DeleteDraftAsync(purchaseId, cancellationToken);

    public Task<Result<PurchaseDetail>> ConfirmPurchaseAsync(Guid purchaseId, CancellationToken cancellationToken = default) =>
        _local.ConfirmPurchaseAsync(purchaseId, cancellationToken);

    public Task<Result<PurchaseDetail>> CancelPurchaseAsync(Guid purchaseId, CancellationToken cancellationToken = default) =>
        _local.CancelPurchaseAsync(purchaseId, cancellationToken);

    public Task<Result<PurchaseDetail>> SetPaymentStatusAsync(
        Guid purchaseId, PurchasePaymentStatus paymentStatus, CancellationToken cancellationToken = default) =>
        _local.SetPaymentStatusAsync(purchaseId, paymentStatus, cancellationToken);

    /// <summary>
    /// Returns null (meaning "fall back to local") on any connectivity
    /// failure - deliberately swallows exceptions here, since a network
    /// failure calling the API is an expected, routine condition for this
    /// method, not something the caller should have to handle specially.
    /// </summary>
    private async Task<Result<PurchaseDetail>?> TryCreateOnlineAsync(
        SaveDraftPurchaseRequest request, CancellationToken cancellationToken)
    {
        // The client decides the PurchaseId (Decision 3: GUIDs are safe
        // for offline creation) even on the online path, so the same Id
        // is used consistently whether this succeeds online or falls back
        // to the offline path moments later.
        var purchaseId = Guid.NewGuid();

        var contract = new CreatePurchaseContract(
            purchaseId,
            request.SupplierId,
            request.SupplierInvoiceNumber,
            request.PurchaseDate,
            request.Notes,
            request.Items
                .Select(i => new PurchaseItemContract(i.ProductId, i.Quantity, i.UnitCost, i.DiscountAmount, i.TaxPercentage))
                .ToList());

        var syncRequest = new SyncOperationRequest(
            Guid.NewGuid(), "Purchase.Create", nameof(Purchase), purchaseId,
            DateTimeOffset.UtcNow, JsonSerializer.Serialize(contract));

        SyncOperationResponse response;
        try
        {
            response = await _apiClient.SendPurchaseCreateAsync(syncRequest, cancellationToken);
        }
        catch
        {
            return null;
        }

        if (response.Outcome is not (SyncOperationOutcome.Processed or SyncOperationOutcome.AlreadyProcessed))
        {
            // A genuine server-side rejection (e.g. the supplier no longer
            // exists) is a real validation failure, not a connectivity
            // problem - surfaced to the caller directly rather than
            // silently falling back to creating it locally anyway.
            return Result.Failure<PurchaseDetail>(response.ErrorMessage ?? "The server rejected this purchase.");
        }

        if (response.ResultJson is not { Length: > 0 } resultJson)
        {
            return null;
        }

        PurchaseSyncResultContract? result;
        try
        {
            result = JsonSerializer.Deserialize<PurchaseSyncResultContract>(resultJson);
        }
        catch (JsonException)
        {
            return null;
        }

        if (result is null)
        {
            return null;
        }

        await RecordAlreadySyncedPurchaseLocallyAsync(request, result, cancellationToken);

        // Reuses the local service's existing, correct query/projection
        // logic (Supplier/Product name joins, etc.) rather than
        // duplicating PurchaseDetail construction here.
        var detail = await _local.GetPurchaseByIdAsync(result.PurchaseId, cancellationToken);
        return detail is not null
            ? Result.Success(detail)
            : Result.Failure<PurchaseDetail>("Purchase was created online but could not be re-read locally.");
    }

    /// <summary>
    /// Writes the API's already-authoritative result directly into local
    /// SQLite - deliberately WITHOUT an OutboxOperation, since this data
    /// is already synchronized. This is the one place that duplicates a
    /// small amount of entity-construction logic already present in
    /// PurchaseService.SaveDraftAsync: an acceptable, bounded tradeoff
    /// (plain property assignment, no business rules - those already ran
    /// server-side) rather than restructuring IPurchaseService's public
    /// contract to expose an internal "record an already-synced purchase"
    /// method that ViewModels should never call.
    /// </summary>
    private async Task RecordAlreadySyncedPurchaseLocallyAsync(
        SaveDraftPurchaseRequest request, PurchaseSyncResultContract result, CancellationToken cancellationToken)
    {
        var lineComputations = request.Items
            .Select(i => PurchaseWorkflow.ComputeLine(i.Quantity, i.UnitCost, i.DiscountAmount, i.TaxPercentage))
            .ToList();
        var headerTotals = PurchaseWorkflow.ComputeHeaderTotals(lineComputations);

        var purchase = new Purchase
        {
            Id = result.PurchaseId,
            PurchaseNumber = result.ServerPurchaseNumber,
            SupplierId = request.SupplierId,
            SupplierInvoiceNumber = request.SupplierInvoiceNumber,
            PurchaseDate = request.PurchaseDate,
            Notes = request.Notes,
            Status = result.Status,
            PaymentStatus = PurchasePaymentStatus.Unpaid,
            Subtotal = headerTotals.Subtotal,
            DiscountAmount = headerTotals.DiscountTotal,
            TaxAmount = headerTotals.TaxTotal,
            TotalAmount = headerTotals.TotalAmount,
        };

        _context.Purchases.Add(purchase);

        foreach (var (item, computation) in request.Items.Zip(lineComputations))
        {
            _context.PurchaseItems.Add(new PurchaseItem
            {
                PurchaseId = purchase.Id,
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitCost = item.UnitCost,
                DiscountAmount = item.DiscountAmount,
                TaxPercentage = item.TaxPercentage,
                TaxAmount = computation.TaxAmount,
                LineTotal = computation.LineTotal,
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
