using InventoryManagement.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Api.Health;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // Liveness only - "is the process up" - deliberately does not
        // touch the database, so it stays fast and meaningful even if
        // PostgreSQL itself is having trouble.
        app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timeUtc = DateTimeOffset.UtcNow }))
            .WithName("Health");

        // Separate readiness check that DOES touch PostgreSQL - use this
        // one specifically to verify database connectivity (item 7 of the
        // requested post-implementation verification: "Verify PostgreSQL
        // connectivity").
        app.MapGet("/api/health/database", async (InventoryPostgresDbContext context, CancellationToken cancellationToken) =>
        {
            try
            {
                var canConnect = await context.Database.CanConnectAsync(cancellationToken);
                return canConnect
                    ? Results.Ok(new { status = "connected" })
                    : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    title: "Database connectivity check failed",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .WithName("DatabaseHealth");

        return app;
    }
}
