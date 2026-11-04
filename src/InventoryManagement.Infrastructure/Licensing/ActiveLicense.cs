using InventoryManagement.Application.Licensing;
using InventoryManagement.Licensing;

namespace InventoryManagement.Infrastructure.Licensing;

/// <summary>
/// Supplies the password backup files are encrypted with. Separate from
/// <see cref="ILicenseInfo"/> because the key is a secret only the backup
/// code needs - it must never reach a ViewModel or the screen.
/// </summary>
public interface IBackupKeyProvider
{
    string BackupPassword { get; }
}

/// <summary>The validated license for this run, registered as a singleton by the composition root.</summary>
public sealed class ActiveLicense : ILicenseInfo, IBackupKeyProvider
{
    private readonly LicenseData _license;

    public ActiveLicense(LicenseData license)
    {
        _license = license;
    }

    public Guid LicenseId => _license.LicenseId;

    public string CustomerId => _license.CustomerId;

    public string CustomerName => _license.CustomerName;

    public string MachineId => _license.MachineId;

    public DateTimeOffset IssuedAtUtc => _license.IssuedAtUtc;

    public string BackupPassword => _license.BackupKey;
}
