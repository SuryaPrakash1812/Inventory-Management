namespace InventoryManagement.Application.Settings;

/// <summary>
/// How the application should decide its light/dark appearance.
/// </summary>
public enum ThemeMode
{
    /// <summary>Follow the Windows setting.</summary>
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>
/// The full set of persisted user preferences. Kept as a plain, serializable
/// POCO with no dependency on WPF or any specific storage mechanism - the UI
/// layer maps <see cref="ThemeMode"/> onto whatever theming API it uses.
///
/// Every property has a usable default, so a settings file written by an
/// older version (missing the newer properties) still loads correctly.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Light by default; users can switch on the Settings page.</summary>
    public ThemeMode Theme { get; set; } = ThemeMode.Light;

    public CompanyProfile Company { get; set; } = new();

    public BackupPreferences Backup { get; set; } = new();
}

/// <summary>The customer's own business details, printed at the top of exported reports.</summary>
public sealed class CompanyProfile
{
    public string? Name { get; set; }

    public string? Address { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    /// <summary>GSTIN / VAT / tax registration number, if any.</summary>
    public string? TaxId { get; set; }
}

public sealed class BackupPreferences
{
    public const int MinimumIntervalHours = 1;
    public const int MinimumRetentionCount = 1;

    /// <summary>Folder backups are written to; null means the default AppData\InventoryManagement\Backups.</summary>
    public string? Folder { get; set; }

    public bool AutomaticBackupsEnabled { get; set; } = true;

    public int AutomaticIntervalHours { get; set; } = 24;

    /// <summary>How many AUTOMATIC backups to keep; older ones are deleted. Manual backups are never deleted.</summary>
    public int RetentionCount { get; set; } = 10;
}
