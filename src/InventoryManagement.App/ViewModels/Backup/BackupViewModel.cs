using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InventoryManagement.Application.Backup;
using Microsoft.Win32;

namespace InventoryManagement.App.ViewModels.Backup;

public sealed partial class BackupViewModel : ViewModelBase
{
    private readonly IBackupService _backupService;

    public ObservableCollection<BackupRecordSummary> BackupHistory { get; } = new();

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _errorMessage;

    public bool IsNotBusy => !IsBusy;

    public BackupViewModel(IBackupService backupService)
    {
        _backupService = backupService;
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
            var result = await _backupService.CreateBackupAsync();
            if (result.IsFailure)
            {
                ErrorMessage = result.Error;
                return;
            }

            StatusMessage = $"Backup '{result.Value.FileName}' created successfully.";
            await RefreshHistoryAsync();
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsNotBusy));
        }
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
            Filter = "SQLite database (*.db)|*.db|All files (*.*)|*.*",
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

            StatusMessage = "Restore staged successfully. Close and reopen the application to complete the restore.";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsNotBusy));
        }
    }
}
