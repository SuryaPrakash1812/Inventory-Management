using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InventoryManagement.Infrastructure.Licensing;
using InventoryManagement.Licensing;
using Microsoft.Win32;

namespace InventoryManagement.App.ViewModels.Licensing;

/// <summary>
/// Backs the activation screen shown at startup when this PC has no valid
/// license. The customer sends the displayed Machine ID to the vendor, gets
/// back a license.lic issued for exactly that ID, and loads it here. Not
/// resolved through DI: it runs before the host (and the database) exist,
/// since nothing else may start until the license is valid.
/// </summary>
public sealed partial class ActivationViewModel : ViewModelBase
{
    private readonly LicenseStore _licenseStore;

    public string MachineId => _licenseStore.MachineId;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Set once a valid license has been installed; the window closes with DialogResult=true.</summary>
    public LicenseData? ActivatedLicense { get; private set; }

    public event EventHandler? Activated;

    public ActivationViewModel(LicenseStore licenseStore, string? initialError)
    {
        _licenseStore = licenseStore;

        // "No license yet" is the normal first-run state, not an error worth showing in red.
        _errorMessage = File.Exists(Infrastructure.Common.AppPaths.LicenseFilePath) ? initialError : null;
    }

    [RelayCommand]
    private void CopyMachineId()
    {
        Clipboard.SetText(MachineId);
        StatusMessage = "Machine ID copied to the clipboard.";
    }

    [RelayCommand]
    private void LoadLicenseFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select your license file",
            Filter = "License file (*.lic)|*.lic|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        string text;
        try
        {
            text = File.ReadAllText(dialog.FileName);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"The file could not be read: {ex.Message}";
            return;
        }

        var result = _licenseStore.Install(text);
        if (!result.IsValid)
        {
            StatusMessage = null;
            ErrorMessage = result.Error;
            return;
        }

        ErrorMessage = null;
        ActivatedLicense = result.License;
        Activated?.Invoke(this, EventArgs.Empty);
    }
}
