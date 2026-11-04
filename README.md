# Inventory Management

A production-quality, offline-first Windows desktop application for inventory
management. WPF + MVVM on .NET 8, with an encrypted SQLite (SQLCipher) local
database and a clean, modular architecture.

## Status

The offline application is feature-complete. Everything works with no
network connection:

- **Licensing:** each copy is locked to one PC by a vendor-signed license.
  The database and backups are encrypted, so copying the program, its data or
  its backups to another PC does not work. See [docs/Licensing.md](docs/Licensing.md)
  for how the protection works and how to issue licenses.
- **Users, roles and permissions:** first-run administrator setup, sign-in,
  and a permission-filtered navigation menu.
- **Products and categories:** CRUD, search, filter, sort and paging, plus CSV
  import and export.
- **Suppliers and customers:** CRUD, search and deactivation.
- **Purchases:** draft, confirm (adds stock), cancel (reverses stock) and
  payment status.
- **Sales:** draft, invoice (stock-checked, removes stock) and cancel
  (returns stock).
- **Inventory and stock adjustments:** stock history and low-stock list;
  confirm and cancel adjustments.
- **Dashboard and reports:** inventory, low stock, purchases, sales and stock
  movements, with date filters, totals and CSV export.
- **Backup and restore:** manual and scheduled automatic backups, retention,
  a configurable folder, and encrypted, verified restore that rejects backups
  from another customer or a newer version.
- **Settings:** theme, company details (printed on exported reports), backup
  preferences and license information.

Not yet done: online sync beyond creating purchases, cloud backup providers,
the polish pass, and an installer (stages 12-14).

## Architecture

```
InventoryManagement/
├── src/
│   ├── InventoryManagement.Core            # Result<T>, guard clauses, base exceptions. No dependencies.
│   ├── InventoryManagement.Domain          # Entities, value objects, domain rules. Depends on Core only.
│   ├── InventoryManagement.Application     # Use-case interfaces/services, DTOs. Depends on Domain.
│   ├── InventoryManagement.Infrastructure  # EF Core/SQLCipher, logging, backup, encryption. Depends on Application.
│   ├── InventoryManagement.Licensing       # License signing/verification and Machine ID. BCL only.
│   ├── InventoryManagement.App             # WPF shell, Views, ViewModels, composition root (DI + hosting).
│   └── (Contracts / Sync / Api / Infrastructure.Postgres - online sync, not required offline)
├── tools/
│   └── InventoryManagement.LicenseGenerator # VENDOR ONLY - issues customer licenses. Never ship.
└── tests/
    ├── InventoryManagement.Domain.Tests
    ├── InventoryManagement.Infrastructure.Tests
    └── InventoryManagement.Sync.Tests
```

Dependencies only ever point inward (`App -> Infrastructure -> Application -> Domain -> Core`).
Nothing in `Domain` or `Application` knows that WPF, EF Core, or SQLite exist.

Feature verticals (Products, Sales, Purchases, Inventory, Reports, Backup, ...)
are added under their own folders in later stages, each with its own
Domain/Application/Infrastructure/UI slice, so features can be worked on
independently without touching unrelated code.

## Requirements to build

- Windows 10/11
- .NET 8 SDK
- Visual Studio 2022 (17.8+) with the ".NET Desktop Development" workload,
  or `dotnet` CLI

## Build & run

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project src\InventoryManagement.App\InventoryManagement.App.csproj
```

On first run the app shows its activation screen. To license your own
development PC:

```powershell
dotnet run --project tools\InventoryManagement.LicenseGenerator -- machine-id
dotnet run --project tools\InventoryManagement.LicenseGenerator -- issue --keys keys\vendor-keys.json --customer-id DEV --customer-name "Development" --machine-id <ID printed above> --out keys\dev.lic
```

Then load `keys\dev.lic` on the activation screen.

## Stage plan

1. **Foundation** - solution/project skeleton, DI, logging, app shell *(complete)*
2. **Data layer** - SQLite + EF Core, base entities, migrations *(complete)*
3. **Products & Categories** *(complete)*
4. **Suppliers & Customers** *(complete)*
5. **Purchases** *(complete)*
6. **Sales** *(complete)*
7. **Inventory & Stock Adjustments** *(complete)*
8. **Dashboard & Reports** *(complete)*
9. **Users, Auth, Permissions** *(complete)*
10. **Settings** *(complete)*
11. **Backup/Restore engine** (local provider) *(complete)*
    - **Licensing & data encryption** *(complete - see docs/Licensing.md)*
12. **Cloud backup providers** (Google Drive, OneDrive)
13. **Polish pass** - themes, shortcuts, accessibility, performance
14. **Packaging/installer**

Each stage is implemented, built, tested, and summarized before the next one
begins.
