using InventoryManagement.Api.Health;
using InventoryManagement.Api.Purchases;
using InventoryManagement.Infrastructure.Postgres;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException(
        "Missing ConnectionStrings:Postgres configuration. Set it in appsettings.json, " +
        "appsettings.Development.json, or the ConnectionStrings__Postgres environment variable.");

builder.Services.AddInfrastructurePostgres(connectionString);

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

app.MapHealthEndpoints();
app.MapPurchaseSyncEndpoints();

app.Run();

// Exposed for WebApplicationFactory-based integration tests (Api.Tests).
public partial class Program
{
}
