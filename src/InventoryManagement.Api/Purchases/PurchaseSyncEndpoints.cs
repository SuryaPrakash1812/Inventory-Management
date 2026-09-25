using InventoryManagement.Contracts.Sync;
using InventoryManagement.Infrastructure.Postgres.Purchases;

namespace InventoryManagement.Api.Purchases;

/// <summary>
/// Business-oriented endpoints for the Purchase module, per the
/// architecture decision to prefer POST /api/purchases-style endpoints
/// over generic entity CRUD. Deliberately thin: this file only translates
/// HTTP in and out - PurchaseSyncService (Infrastructure.Postgres) does
/// everything else, including idempotency and validation.
/// </summary>
public static class PurchaseSyncEndpoints
{
    public static IEndpointRouteBuilder MapPurchaseSyncEndpoints(this IEndpointRouteBuilder app)
    {
        // A single generic "sync operation" endpoint rather than one route
        // per operation type - OperationType inside the envelope says what
        // it is. This matches the Outbox's own shape (one queue of typed
        // operations) and keeps the route surface small as more operation
        // types are added later, without needing a new endpoint each time.
        app.MapPost("/api/sync/purchases", async (
            SyncOperationRequest request,
            PurchaseSyncService syncService,
            CancellationToken cancellationToken) =>
        {
            // Only "Purchase.Create" is implemented in this foundation
            // step - Confirm/Cancel sync handlers are deliberately not
            // built out yet (see PurchaseSyncService remarks).
            if (request.OperationType != "Purchase.Create")
            {
                return Results.BadRequest(new SyncOperationResponse(
                    request.OperationId,
                    SyncOperationOutcome.ValidationFailed,
                    $"Unsupported OperationType '{request.OperationType}' for this endpoint.",
                    null));
            }

            var response = await syncService.CreatePurchaseAsync(request, cancellationToken);
            return Results.Ok(response);
        })
        .WithName("SyncPurchase")
        .WithSummary("Accepts a client-generated Purchase.Create sync operation, idempotently.");

        return app;
    }
}
