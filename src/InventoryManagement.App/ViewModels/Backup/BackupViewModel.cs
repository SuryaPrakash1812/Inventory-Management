using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InventoryManagement.Application.Backup;
using InventoryManagement.Application.Settings;
using InventoryManagement.Infrastructure.Common;
using Microsoft.Win32;

namespace InventoryManagement.App.ViewModels.Backup;

public sealed partial class BackupViewModel : ViewModelBase
{
    private readonly IBackupService _backupService;
    private readonly ISettingsService _settingsService;

    public ObservableCollection<BackupRecordSummary> BackupHistory { get; } = new();

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _scheduleDescription;

    public bool IsNotBusy => !IsBusy;

    public string BackupFolder =>
        string.IsNullOrWhiteSpace(_settingsService.Current.Backup.Folder) ? AppPaths.BackupsFolder : _settingsService.Current.Backup.Folder!;

    public BackupViewModel(IBackupService backupService, ISettingsService settingsService)
    {
        _backupService = backupService;
        _settingsService = settingsService;
    }

    public async Task InitializeAsync() => await RefreshHistoryAsync();

    private async Task RefreshHistoryAsync()
    {
        var history = await _backupService.GetBackupHistoryAsync();

        BackupHistory.Clear();
        foreach (var record in history)
        {
            BackupHistory.Add(record);
        }

        var preferences = _settingsService.Current.Backup;
        if (!preferences.AutomaticBackupsEnabled)
        {
            ScheduleDescription = "Automatic backups are turned off (Settings > Backup).";
        }
        else
        {
            var last = await _backupService.GetLastAutomaticBackupTimeAsync();
            ScheduleDescription =
                $"Automatic backup every {preferences.AutomaticIntervalHours} hour(s), keeping the newest {preferences.RetentionCount}. "
                + (last is null ? "No automatic backup has run yet." : $"Last automatic backup: {last.Value.ToLocalTime():g}.");
        }

        OnPropertyChanged(nameof(BackupFolder));
    }

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        StatusMessage = null;
        IsBusy = true;
        OnPropertyChanged(nameof(IsNotBusy));
        try
        {
            var result = await _backupService.CreateBackupAsync(_settingsService.Current.Backup.Folder);
            if (result.IsFailure)
            {
                ErrorMessage = result.Error;
                return;
            }

            StatusMessage = $"Backup '{result.Value.FileName}' created and verified.";
            await RefreshHistoryAsync();
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsNotBusy));
        }
    }

    [RelayCommand]
    private void OpenBackupFolder()
    {
        Directory.CreateDirectory(BackupFolder);
        Process.Start(new ProcessStartInfo { FileName = BackupFolder, UseShellExecute = true });
    }

    /// <summary>
    /// Uses a plain Win32 file-open dialog rather than a custom picker -
    /// this is a desktop file-selection task, not something that needs a
    /// bespoke in-app UI.
    /// </summary>
    [RelayCommand]
    private async Task RestoreFromFileAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Select a backup file to restore",
            InitialDirectory = Directory.Exists(BackupFolder) ? BackupFolder : null,
            Filter = "Inventory Management backup (*.imbak;*.db)|*.imbak;*.db|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        ErrorMessage = null;
        StatusMessage = null;
        IsBusy = true;
        OnPropertyChanged(nameof(IsNotBusy));
        try
        {
            var result = await _backupService.StageRestoreAsync(dialog.FileName);
            if (result.IsFailure)
            {
                ErrorMessage = result.Error;
                return;
            }

            StatusMessage = "Restore staged successfully. Close and reopen the application to complete the restore. "
                + "Your current data will be kept as inventory.before-restore.db.";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsNotBusy));
        }
    }
}
