namespace InventoryManagement.Infrastructure.Common;

/// <summary>
/// Single source of truth for where this application's data lives on disk.
/// Everything is rooted under the current user's AppData folder, so the app
/// never needs admin rights and each Windows user gets their own data.
/// </summary>
public static class AppPaths
{
    public static string AppDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "InventoryManagement");

    public static string LogsFolder { get; } = Path.Combine(AppDataRoot, "logs");

    public static string BackupsFolder { get; } = Path.Combine(AppDataRoot, "Backups");

    public static string DatabaseFilePath { get; } = Path.Combine(AppDataRoot, "inventory.db");

    /// <summary>The DPAPI-protected database encryption key - see DatabaseKeyProvider.</summary>
    public static string DatabaseKeyFilePath { get; } = Path.Combine(AppDataRoot, "inventory.key");

    /// <summary>The customer's signed, machine-bound license - see LicenseStore.</summary>
    public static string LicenseFilePath { get; } = Path.Combine(AppDataRoot, "license.lic");
}
