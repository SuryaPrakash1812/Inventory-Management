using InventoryManagement.Licensing;

namespace InventoryManagement.Infrastructure.Licensing;

/// <summary>
/// Reads and installs the license file. Installing always validates first,
/// so a license for another PC (or an edited one) is rejected at import time
/// with a clear message rather than being saved and failing on next start.
/// </summary>
public sealed class LicenseStore
{
    private readonly string _licenseFilePath;
    private readonly LicenseValidator _validator;
    private readonly string _machineId;

    public LicenseStore(string licenseFilePath, LicenseValidator validator, string machineId)
    {
        _licenseFilePath = licenseFilePath;
        _validator = validator;
        _machineId = machineId;
    }

    public string MachineId => _machineId;

    public LicenseValidationResult ValidateInstalled()
    {
        string? text = null;
        try
        {
            if (File.Exists(_licenseFilePath))
            {
                text = File.ReadAllText(_licenseFilePath);
            }
        }
        catch (IOException)
        {
            // Treated the same as "no license" - the activation screen lets the user install one.
        }

        return _validator.Validate(text, _machineId);
    }

    public LicenseValidationResult Install(string licenseText)
    {
        var result = _validator.Validate(licenseText, _machineId);
        if (!result.IsValid)
        {
            return result;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_licenseFilePath)!);
        var tempPath = _licenseFilePath + ".tmp";
        File.WriteAllText(tempPath, licenseText);
        File.Move(tempPath, _licenseFilePath, overwrite: true);

        return result;
    }
}
