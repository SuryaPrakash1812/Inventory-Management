# Inventory Management

A production-quality, offline-first Windows desktop application for inventory
management. WPF + MVVM on .NET 8, SQLite for local storage (added in Stage 2),
clean modular architecture.

## Status

**Stage 1 - Project Foundation: complete.** This stage delivers the app shell,
navigation, theming, settings, and error-handling infrastructure with
placeholder pages for every planned feature - no inventory business logic yet.

What's in place:

- Solution structure (Core / Domain / Application / Infrastructure / App / Tests)
- WPF + MVVM (CommunityToolkit.Mvvm) + dependency injection (generic Host)
- Structured logging (Serilog: rolling file + debug sink)
- App shell: title bar, sidebar navigation, header (current page title, theme
  toggle, user area), content frame, status bar
- Navigation infrastructure (WPF-UI `NavigationView`, DI-resolved pages)
- Light/Dark/System theme infrastructure, with a working toggle and a
  Settings page that persists the choice
- Global error handling (UI-thread, background-thread, and unobserved-task
  exceptions all logged and shown as a friendly message instead of crashing)
- Settings infrastructure (`ISettingsService` backed by a local JSON file,
  atomic writes, resilient to a corrupt file)
- Placeholder navigation pages for every planned feature: Dashboard, Products,
  Categories, Suppliers, Customers, Purchases, Sales, Inventory, Stock
  Adjustments, Reports, Users, Backup, and a real Settings page

## Architecture

```
InventoryManagement/
├── src/
│   ├── InventoryManagement.Core            # Result<T>, guard clauses, base exceptions. No dependencies.
│   ├── InventoryManagement.Domain          # Entities, value objects, domain rules. Depends on Core only.
│   ├── InventoryManagement.Application     # Use-case interfaces/services, DTOs. Depends on Domain.
│   ├── InventoryManagement.Infrastructure  # EF Core/SQLite, logging, backup providers. Depends on Application.
│   └── InventoryManagement.App             # WPF shell, Views, ViewModels, composition root (DI + hosting).
└── tests/
    └── InventoryManagement.Domain.Tests    # Unit tests for Core/Domain logic.
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

## Stage plan

1. **Foundation** - solution/project skeleton, DI, logging, app shell *(current)*
2. **Data layer** - SQLite + EF Core, base entities, migrations
3. **Products & Categories** - first full vertical slice
4. **Suppliers & Customers**
5. **Purchases**
6. **Sales**
7. **Inventory & Stock Adjustments**
8. **Dashboard & Reports**
9. **Users, Auth, Permissions**
10. **Settings**
11. **Backup/Restore engine** (local provider)
12. **Cloud backup providers** (Google Drive, OneDrive)
13. **Polish pass** - themes, shortcuts, accessibility, performance
14. **Packaging/installer**

Each stage is implemented, built, tested, and summarized before the next one
begins.
