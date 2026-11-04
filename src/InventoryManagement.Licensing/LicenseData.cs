using System.Text.Json.Serialization;

namespace InventoryManagement.Licensing;

/// <summary>
/// Everything a license grants, exactly as signed by the vendor. A license
/// is bound to ONE machine (<see cref="MachineId"/>): copying the license
/// file - or the whole application folder - to another PC fails validation
/// there, because that PC computes a different machine ID.
///
/// <see cref="BackupKey"/> is derived by the License Generator from the
/// vendor's secret and <see cref="CustomerId"/>, so every license issued to
/// the same customer (e.g. a replacement license after a hardware change)
/// carries the same key. That is what lets a customer restore their own
/// backups onto a new, re-licensed PC, while a backup handed to anyone else
/// cannot be opened under that person's (different) license.
/// </summary>
public sealed record LicenseData
{
    public const int CurrentFormatVersion = 1;

    [JsonPropertyName("v")]
    public int FormatVersion { get; init; } = CurrentFormatVersion;

    [JsonPropertyName("licenseId")]
    public required Guid LicenseId { get; init; }

    [JsonPropertyName("customerId")]
    public required string CustomerId { get; init; }

    [JsonPropertyName("customerName")]
    public required string CustomerName { get; init; }

    [JsonPropertyName("machineId")]
    public required string MachineId { get; init; }

    [JsonPropertyName("issuedAtUtc")]
    public required DateTimeOffset IssuedAtUtc { get; init; }

    /// <summary>Base64 of a 32-byte key used to encrypt this customer's backup files.</summary>
    [JsonPropertyName("backupKey")]
    public required string BackupKey { get; init; }
}
