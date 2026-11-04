# Local database

The app stores its data in `%AppData%\InventoryManagement\inventory.db`, a
SQLite database **encrypted with SQLCipher**. The key lives in
`inventory.key`, protected by Windows DPAPI for this PC. See
`docs/Licensing.md`.

Startup order (see `App.xaml.cs`):

1. License check (activation screen if needed).
2. `StagedRestore.ApplyIfPending`: swaps in a restore requested last session.
3. `LocalDatabaseBootstrapper.PrepareAsync`: unlocks the database. On the first
   run after upgrading from an unencrypted version, it also encrypts the
   existing data in place.
4. `DatabaseInitializer`: applies pending EF Core migrations
   (`Database.MigrateAsync()`). It creates the database if it doesn't exist.

## Adding a migration

Whenever the model changes (new entity, property or index):

```powershell
dotnet ef migrations add AddSomeNewThing `
    --project src\InventoryManagement.Infrastructure `
    --startup-project src\InventoryManagement.App
```

`dotnet ef` finds the DbContext through `InventoryDbContextFactory`. That
factory has no encryption key, which is fine for `migrations add` (it never
opens the file). `dotnet ef database update` against the real, encrypted
database will not work. You don't need it, because the app applies migrations
itself on startup.

## Inspecting the data

DB Browser for SQLite cannot open the encrypted file. That is intended. For
debugging, use a backup taken on a licensed PC and open it with a
SQLCipher-capable tool, using that customer's backup key.
