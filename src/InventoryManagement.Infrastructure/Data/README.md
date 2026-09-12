# Database (Stage 2) - one-time setup

This project's initial EF Core migration can't be generated inside the build
sandbox that authored this stage (no .NET SDK / EF tooling available there).
Run this once, from the repository root, on a machine with the .NET 8 SDK:

```powershell
dotnet tool install --global dotnet-ef   # skip if already installed
dotnet ef migrations add InitialCreate `
    --project src\InventoryManagement.Infrastructure `
    --startup-project src\InventoryManagement.App
```

This generates `src/InventoryManagement.Infrastructure/Migrations/*.cs` and
commits the initial schema (including the seeded Roles/Permissions reference
data) as a real, versioned migration.

From then on, nothing further is needed - `DatabaseInitializer` calls
`Database.MigrateAsync()` automatically every time the app starts, applying
any pending migrations (including this first one) and creating
`%AppData%\InventoryManagement\inventory.db` if it doesn't exist yet.

## Adding a migration later

Whenever the model changes (new entity, new property, new index), generate a
new migration the same way with a descriptive name:

```powershell
dotnet ef migrations add AddSomeNewThing `
    --project src\InventoryManagement.Infrastructure `
    --startup-project src\InventoryManagement.App
```

`dotnet ef` finds the DbContext via `InventoryDbContextFactory`
(`IDesignTimeDbContextFactory<InventoryDbContext>`), so this works
regardless of the WPF app's own startup code.
