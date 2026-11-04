using InventoryManagement.Infrastructure.Licensing;
using InventoryManagement.Licensing;
using Xunit;

namespace InventoryManagement.Infrastructure.Tests.Licensing;

/// <summary>
/// Uses a throwaway vendor key pair generated per test run - never the real
/// vendor key - and machine IDs computed from fixed fake components, so the
/// tests behave the same on any PC.
/// </summary>
public class LicensingTests : IDisposable
{
    private static readonly string ThisPc = MachineIdentity.FromComponents(new[] { "guid-of-pc-1", "Dell Inc.", "OptiPlex 7010" });
    private static readonly string OtherPc = MachineIdentity.FromComponents(new[] { "guid-of-pc-2", "Dell Inc.", "OptiPlex 7010" });

    private readonly VendorKeys _vendor = VendorKeys.CreateNew();
    private readonly LicenseValidator _validator;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"InventoryManagementLicenseTests_{Guid.NewGuid()}");

    public LicensingTests()
    {
        _validator = new LicenseValidator(_vendor.PublicKeyPem);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void MachineId_IsStable_FormattedInGroups_AndDiffersBetweenPcs()
    {
        Assert.Matches("^[A-Z2-9]{5}(-[A-Z2-9]{5}){4}$", ThisPc);
        Assert.Equal(ThisPc, MachineIdentity.FromComponents(new[] { "GUID-OF-PC-1 ", "dell inc.", "optiplex 7010" }));
        Assert.NotEqual(ThisPc, OtherPc);
    }

    [Fact]
    public void MachineId_TryParse_AcceptsTypedVariants_AndRejectsGarbage()
    {
        Assert.True(MachineIdentity.TryParse(ThisPc.Replace("-", string.Empty).ToLowerInvariant(), out var parsed));
        Assert.Equal(ThisPc, parsed);
        Assert.False(MachineIdentity.TryParse("not-a-machine-id", out _));
        Assert.False(MachineIdentity.TryParse("00000-00000-00000-00000-00000", out _)); // 0 and 1 are not in the alphabet
    }

    [Fact]
    public void IssuedLicense_ValidatesOnTheMachineItWasIssuedFor()
    {
        var text = _vendor.Issue("C001", "Acme Traders", ThisPc, DateTimeOffset.UtcNow);

        var result = _validator.Validate(text, ThisPc);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("Acme Traders", result.License!.CustomerName);
        Assert.Equal("C001", result.License.CustomerId);
    }

    [Fact]
    public void License_CopiedToAnotherPc_IsRejected()
    {
        var text = _vendor.Issue("C001", "Acme Traders", ThisPc, DateTimeOffset.UtcNow);

        var result = _validator.Validate(text, OtherPc);

        Assert.False(result.IsValid);
        Assert.Contains("different PC", result.Error);
    }

    [Fact]
    public void License_EditedByHand_IsRejected()
    {
        var text = _vendor.Issue("C001", "Acme Traders", ThisPc, DateTimeOffset.UtcNow);
        var lines = text.Split('\n');
        var payloadAndSignature = lines[1].Trim().Split('.');

        // Re-encode the payload with another machine ID but keep the original signature.
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(
            payloadAndSignature[0].Replace('-', '+').Replace('_', '/').PadRight((payloadAndSignature[0].Length + 3) / 4 * 4, '=')));
        var forgedJson = json.Replace(ThisPc, OtherPc);
        var forgedPayload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(forgedJson)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var forged = text.Replace(payloadAndSignature[0], forgedPayload);

        var result = _validator.Validate(forged, OtherPc);

        Assert.False(result.IsValid);
        Assert.Contains("signature", result.Error);
    }

    [Fact]
    public void License_SignedWithAnotherVendorKey_IsRejected()
    {
        var impostor = VendorKeys.CreateNew();
        var text = impostor.Issue("C001", "Acme Traders", ThisPc, DateTimeOffset.UtcNow);

        Assert.False(_validator.Validate(text, ThisPc).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("-----BEGIN INVENTORY MANAGEMENT LICENSE-----\nabc.def\n-----END INVENTORY MANAGEMENT LICENSE-----")]
    public void MissingOrMalformedLicense_IsRejected_WithoutThrowing(string? text)
    {
        var result = _validator.Validate(text, ThisPc);

        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public void BackupKey_IsTheSameForEveryLicenseOfOneCustomer_AndDiffersBetweenCustomers()
    {
        var first = _validator.Validate(_vendor.Issue("C001", "Acme", ThisPc, DateTimeOffset.UtcNow), ThisPc).License!;
        var replacement = new LicenseValidator(_vendor.PublicKeyPem)
            .Validate(_vendor.Issue("c001", "Acme", OtherPc, DateTimeOffset.UtcNow), OtherPc).License!;
        var otherCustomer = _validator.Validate(_vendor.Issue("C002", "Other", ThisPc, DateTimeOffset.UtcNow), ThisPc).License!;

        Assert.Equal(first.BackupKey, replacement.BackupKey);
        Assert.NotEqual(first.BackupKey, otherCustomer.BackupKey);
        Assert.NotEqual(first.LicenseId, replacement.LicenseId);
    }

    [Fact]
    public void Issue_RejectsInvalidMachineId()
    {
        Assert.Throws<ArgumentException>(() => _vendor.Issue("C001", "Acme", "12345", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void LicenseStore_Install_SavesOnlyValidLicenses()
    {
        var path = Path.Combine(_folder, "license.lic");
        var store = new LicenseStore(path, _validator, ThisPc);

        var wrongPc = store.Install(_vendor.Issue("C001", "Acme", OtherPc, DateTimeOffset.UtcNow));
        Assert.False(wrongPc.IsValid);
        Assert.False(File.Exists(path));
        Assert.False(store.ValidateInstalled().IsValid);

        var installed = store.Install(_vendor.Issue("C001", "Acme", ThisPc, DateTimeOffset.UtcNow));
        Assert.True(installed.IsValid);
        Assert.True(store.ValidateInstalled().IsValid);

        // The same installed file, read on another PC, fails.
        Assert.False(new LicenseStore(path, _validator, OtherPc).ValidateInstalled().IsValid);
    }

    [Fact]
    public void ProductPublicKey_IsAValidEcdsaKey()
    {
        using var key = System.Security.Cryptography.ECDsa.Create();
        key.ImportFromPem(ProductPublicKey.Pem);
        Assert.Equal(256, key.KeySize);
    }
}
