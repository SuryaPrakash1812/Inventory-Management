using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InventoryManagement.Licensing;

/// <summary>
/// The vendor-side secrets: the ECDSA private key that signs licenses, and
/// the secret that customer backup keys are derived from. Only the License
/// Generator ever loads this. It must NEVER ship with the application or be
/// committed to source control - anyone holding it can issue licenses.
/// </summary>
public sealed class VendorKeys
{
    [JsonPropertyName("privateKeyPem")]
    public required string PrivateKeyPem { get; init; }

    [JsonPropertyName("publicKeyPem")]
    public required string PublicKeyPem { get; init; }

    /// <summary>Base64 of 32 random bytes.</summary>
    [JsonPropertyName("backupSecret")]
    public required string BackupSecret { get; init; }

    public static VendorKeys CreateNew()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        return new VendorKeys
        {
            PrivateKeyPem = key.ExportPkcs8PrivateKeyPem(),
            PublicKeyPem = key.ExportSubjectPublicKeyInfoPem(),
            BackupSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        };
    }

    public static VendorKeys Load(string path) =>
        JsonSerializer.Deserialize<VendorKeys>(File.ReadAllText(path))
            ?? throw new InvalidDataException($"'{path}' is not a valid vendor key file.");

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));

    /// <summary>Same customer ID in, same key out - see <see cref="LicenseData.BackupKey"/>.</summary>
    public string DeriveBackupKey(string customerId)
    {
        var secret = Convert.FromBase64String(BackupSecret);
        var input = Encoding.UTF8.GetBytes("backup-key:v1:" + customerId.Trim().ToUpperInvariant());
        return Convert.ToBase64String(HMACSHA256.HashData(secret, input));
    }

    public string Issue(string customerId, string customerName, string machineId, DateTimeOffset issuedAtUtc)
    {
        if (!MachineIdentity.TryParse(machineId, out var canonicalMachineId))
        {
            throw new ArgumentException($"'{machineId}' is not a valid Machine ID (expected 25 characters, e.g. ABCDE-FGHJK-LMNPQ-RSTUV-WXYZ2).", nameof(machineId));
        }

        if (string.IsNullOrWhiteSpace(customerId) || string.IsNullOrWhiteSpace(customerName))
        {
            throw new ArgumentException("Customer ID and customer name are both required.");
        }

        var license = new LicenseData
        {
            LicenseId = Guid.NewGuid(),
            CustomerId = customerId.Trim(),
            CustomerName = customerName.Trim(),
            MachineId = canonicalMachineId,
            IssuedAtUtc = issuedAtUtc,
            BackupKey = DeriveBackupKey(customerId),
        };

        using var privateKey = ECDsa.Create();
        privateKey.ImportFromPem(PrivateKeyPem);
        return LicenseCodec.Encode(license, privateKey);
    }
}
