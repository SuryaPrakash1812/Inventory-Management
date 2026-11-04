using InventoryManagement.Application.Backup;
using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InventoryManagement.Infrastructure.Backup;

/// <summary>
/// Takes automatic backups on the interval set in Settings, then applies the
/// retention limit. Checks shortly after startup and then every 15 minutes;
/// each check only backs up if the last successful automatic backup is older
/// than the interval, so restarting the app often never creates extra
/// backups, and a PC that was off for a week gets one backup when it comes
/// back rather than a burst of missed ones.
///
/// Every run uses its OWN DI scope - and therefore its own InventoryDbContext
/// - never the app's long-lived UI scope: a DbContext is not thread-safe, and
/// this runs on a timer thread while the user may be working. SQLite's
/// busy_timeout (see SqliteConnectionInterceptor) handles the brief write
/// contention when the backup's history row is saved.
/// </summary>
public sealed class AutomaticBackupScheduler : IDisposable
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISettingsService _settingsService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<AutomaticBackupScheduler> _logger;
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private Timer? _timer;

    public AutomaticBackupScheduler(
        IServiceScopeFactory scopeFactory,
        ISettingsService settingsService,
        IDateTimeProvider dateTimeProvider,
        ILogger<AutomaticBackupScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _settingsService = settingsService;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public void Start()
    {
        _timer ??= new Timer(_ => _ = RunIfDueAsync(), null, FirstCheckDelay, CheckInterval);
    }

    /// <summary>Public so it can be exercised directly by tests without waiting on the timer.</summary>
    public async Task<bool> RunIfDueAsync(CancellationToken cancellationToken = default)
    {
        if (!await _runLock.WaitAsync(0, cancellationToken))
        {
            return false; // A previous run is still going.
        }

        try
        {
            var preferences = _settingsService.Current.Backup;
            if (!preferences.AutomaticBackupsEnabled)
            {
                return false;
            }

            using var scope = _scopeFactory.CreateScope();
            var backupService = scope.ServiceProvider.GetRequiredService<IBackupService>();

            var interval = TimeSpan.FromHours(Math.Max(BackupPreferences.MinimumIntervalHours, preferences.AutomaticIntervalHours));
            var last = await backupService.GetLastAutomaticBackupTimeAsync(cancellationToken);
            if (last is not null && _dateTimeProvider.UtcNow - last.Value < interval)
            {
                return false;
            }

            var result = await backupService.CreateBackupAsync(preferences.Folder, isAutomatic: true, cancellationToken);
            if (result.IsFailure)
            {
                _logger.LogWarning("Automatic backup failed: {Error}", result.Error);
                return false;
            }

            var removed = await backupService.PruneAutomaticBackupsAsync(preferences.RetentionCount, cancellationToken);
            _logger.LogInformation(
                "Automatic backup {FileName} created; {Removed} old automatic backup(s) removed",
                result.Value.FileName, removed);
            return true;
        }
        catch (Exception ex)
        {
            // Never let a background backup failure take the app down - it
            // will simply be retried at the next check.
            _logger.LogError(ex, "Automatic backup run failed");
            return false;
        }
        finally
        {
            _runLock.Release();
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _runLock.Dispose();
    }
}
