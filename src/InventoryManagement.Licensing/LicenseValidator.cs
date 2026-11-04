using System.Security.Cryptography;

namespace InventoryManagement.Licensing;

public sealed record LicenseValidationResult(LicenseData? License, string? Error)
{
    public bool IsValid => License is not null;
}

/// <summary>
/// The single check the application runs at startup: is this license text
/// genuinely signed by the vendor, AND was it issued for this PC?
/// </summary>
public sealed class LicenseValidator
{
    private readonly string _publicKeyPem;

    /// <summary>Uses the vendor public key compiled into the application.</summary>
    public LicenseValidator()
        : this(ProductPublicKey.Pem)
    {
    }

    /// <summary>Lets tests (and the generator's self-check) verify against a different key pair.</summary>
    public LicenseValidator(string publicKeyPem)
    {
        _publicKeyPem = publicKeyPem;
    }

    public LicenseValidationResult Validate(string? licenseText, string currentMachineId)
    {
        if (string.IsNullOrWhiteSpace(licenseText))
        {
            return new LicenseValidationResult(null, "No license has been installed on this PC.");
        }

        using var publicKey = ECDsa.Create();
        publicKey.ImportFromPem(_publicKeyPem);

        var license = LicenseCodec.TryDecode(licenseText, publicKey, out var error);
        if (license is null)
        {
            return new LicenseValidationResult(null, error);
        }

        if (!MachineIdentity.AreEqual(license.MachineId, currentMachineId))
        {
            return new LicenseValidationResult(
                null,
                "This license was issued for a different PC. Send this PC's Machine ID to your vendor to get a license for it.");
        }

        return new LicenseValidationResult(license, null);
    }
}
