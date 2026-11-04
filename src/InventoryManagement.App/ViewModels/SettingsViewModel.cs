using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InventoryManagement.App.Services;
using InventoryManagement.Application.Licensing;
using InventoryManagement.Application.Settings;
using InventoryManagement.Infrastructure.Common;
using Microsoft.Win32;

namespace InventoryManagement.App.ViewModels;

/// <summary>
/// Backs the Settings page: company profile (printed on exported reports),
/// backup preferences, and read-only license details. Fields are edited in
/// place and only saved when the user clicks Save, so a half-typed value is
/// never persisted.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly ILicenseInfo _license;

    public IReadOnlyList<int> AvailableIntervals { get; } = new[] { 1, 4, 6, 12, 24, 48, 168 };

    [ObservableProperty] private string? _companyName;
    [ObservableProperty] private string? _companyAddress;
    [ObservableProperty] private string? _companyPhone;
    [ObservableProperty] private string? _companyEmail;
    [ObservableProperty] private string? _companyTaxId;

    [ObservableProperty] private string? _backupFolder;
    [ObservableProperty] private bool _automaticBackupsEnabled;
    [ObservableProperty] private int _automaticIntervalHours;
    [ObservableProperty] private string _retentionCountText = string.Empty;

    /// <summary>Local confirmation text shown on this page - not the shared shell status bar, since a page never depends on the shell's specific ShellViewModel instance (see MainWindow.OnSelectionChanged remarks).</summary>
    [ObservableProperty]
    private string? _confirmationMessage;

    [ObservableProperty]
    private string? _errorMessage;

    public string LicensedTo => $"{_license.CustomerName} ({_license.CustomerId})";

    public string MachineId => _license.MachineId;

    public string LicenseId => _license.LicenseId.ToString();

    public string LicenseIssued => _license.IssuedAtUtc.ToLocalTime().ToString("d");

    public string DefaultBackupFolder => AppPaths.BackupsFolder;

    public SettingsViewModel(ISettingsService settingsService, ILicenseInfo license)
    {
        _settingsService = settingsService;
        _license = license;

        var current = settingsService.Current;

        _companyName = current.Company.Name;
        _companyAddress = current.Company.Address;
        _companyPhone = current.Company.Phone;
        _companyEmail = current.Company.Email;
        _companyTaxId = current.Company.TaxId;

        _backupFolder = current.Backup.Folder;
        _automaticBackupsEnabled = current.Backup.AutomaticBackupsEnabled;
        _automaticIntervalHours = AvailableIntervals.Contains(current.Backup.AutomaticIntervalHours)
            ? current.Backup.AutomaticIntervalHours
            : 24;
        _retentionCountText = current.Backup.RetentionCount.ToString();
    }

    [RelayCommand]
    private void BrowseBackupFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose where backups are saved",
            InitialDirectory = string.IsNullOrWhiteSpace(BackupFolder) ? AppPaths.BackupsFolder : BackupFolder,
        };

        if (dialog.ShowDialog() == true)
        {
            BackupFolder = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void UseDefaultBackupFolder() => BackupFolder = null;

    [RelayCommand]
    private async Task SaveAsync()
    {
        ConfirmationMessage = null;
        ErrorMessage = null;

        if (!int.TryParse(RetentionCountText, out var retention) || retention < BackupPreferences.MinimumRetentionCount || retention > 365)
        {
            ErrorMessage = "Number of automatic backups to keep must be a whole number between 1 and 365.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(CompanyEmail) && !CompanyEmail.Contains('@'))
        {
            ErrorMessage = "Company email does not look like a valid email address.";
            return;
        }

        var current = _settingsService.Current;
        current.Company.Name = Clean(CompanyName);
        current.Company.Address = Clean(CompanyAddress);
        current.Company.Phone = Clean(CompanyPhone);
        current.Company.Email = Clean(CompanyEmail);
        current.Company.TaxId = Clean(CompanyTaxId);

        current.Backup.Folder = Clean(BackupFolder);
        current.Backup.AutomaticBackupsEnabled = AutomaticBackupsEnabled;
        current.Backup.AutomaticIntervalHours = Math.Max(BackupPreferences.MinimumIntervalHours, AutomaticIntervalHours);
        current.Backup.RetentionCount = retention;

        await _settingsService.SaveAsync();
        ConfirmationMessage = "Settings saved.";
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
