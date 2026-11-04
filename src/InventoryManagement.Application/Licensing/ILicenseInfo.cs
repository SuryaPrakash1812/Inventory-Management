namespace InventoryManagement.Application.Licensing;

/// <summary>
/// The license this installation is running under, for display (Settings
/// page, About information). Read-only: the license is validated once, at
/// startup, before anything else runs - see App.xaml.cs.
/// </summary>
public interface ILicenseInfo
{
    Guid LicenseId { get; }

    string CustomerId { get; }

    string CustomerName { get; }

    string MachineId { get; }

    DateTimeOffset IssuedAtUtc { get; }
}
