using InventoryManagement.Application.Backup;
using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Application.Settings;
using InventoryManagement.Core.Common;
using InventoryManagement.Domain.Enums;
using InventoryManagement.Infrastructure.Backup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InventoryManagement.Infrastructure.Tests.Backup;

public class AutomaticBackupSchedulerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeBackupService _backups = new();
    private readonly FakeSettingsService _settings = new();

    private AutomaticBackupScheduler CreateSut()
    {
        var services = new ServiceCollection();
        services.AddScoped<IBackupService>(_ => _backups);
        return new AutomaticBackupScheduler(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            _settings,
            new FixedClock(Now),
            NullLogger<AutomaticBackupScheduler>.Instance);
    }

    [Fact]
    public async Task FirstRun_WithNoPreviousBackup_CreatesAutomaticBackup_ThenPrunes()
    {
        _settings.Current.Backup.Folder = @"D:\Backups";
        _settings.Current.Backup.RetentionCount = 7;

        var ran = await CreateSut().RunIfDueAsync();

        Assert.True(ran);
        Assert.Equal(@"D:\Backups", Assert.Single(_backups.CreatedIn));
        Assert.True(_backups.LastWasAutomatic);
        Assert.Equal(7, _backups.PrunedKeeping);
    }

    [Fact]
    public async Task RecentBackup_WithinInterval_IsNotRepeated()
    {
        _settings.Current.Backup.AutomaticIntervalHours = 24;
        _backups.LastAutomatic = Now.AddHours(-23);

        Assert.False(await CreateSut().RunIfDueAsync());
        Assert.Empty(_backups.CreatedIn);
    }

    [Fact]
    public async Task OverdueBackup_IsTaken()
    {
        _settings.Current.Backup.AutomaticIntervalHours = 24;
        _backups.LastAutomatic = Now.AddHours(-25);

        Assert.True(await CreateSut().RunIfDueAsync());
    }

    [Fact]
    public async Task Disabled_NeverBacksUp()
    {
        _settings.Current.Backup.AutomaticBackupsEnabled = false;

        Assert.False(await CreateSut().RunIfDueAsync());
        Assert.Empty(_backups.CreatedIn);
    }

    [Fact]
    public async Task FailedBackup_SkipsPruning()
    {
        _backups.FailNext = true;

        Assert.False(await CreateSut().RunIfDueAsync());
        Assert.Null(_backups.PrunedKeeping);
    }

    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new();

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeBackupService : IBackupService
    {
        public DateTimeOffset? LastAutomatic { get; set; }
        public bool FailNext { get; set; }
        public List<string?> CreatedIn { get; } = new();
        public bool LastWasAutomatic { get; private set; }
        public int? PrunedKeeping { get; private set; }

        public Task<IReadOnlyList<BackupRecordSummary>> GetBackupHistoryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BackupRecordSummary>>(Array.Empty<BackupRecordSummary>());

        public Task<Result<BackupRecordSummary>> CreateBackupAsync(
            string? destinationFolder = null, bool isAutomatic = false, CancellationToken cancellationToken = default)
        {
            if (FailNext)
            {
                return Task.FromResult(Result.Failure<BackupRecordSummary>("disk full"));
            }

            CreatedIn.Add(destinationFolder);
            LastWasAutomatic = isAutomatic;
            return Task.FromResult(Result.Success(new BackupRecordSummary(
                Guid.NewGuid(), Now, "b.imbak", "b.imbak", 1, BackupStatus.Success, isAutomatic, null)));
        }

        public Task<DateTimeOffset?> GetLastAutomaticBackupTimeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(LastAutomatic);

        public Task<int> PruneAutomaticBackupsAsync(int keepCount, CancellationToken cancellationToken = default)
        {
            PrunedKeeping = keepCount;
            return Task.FromResult(0);
        }

        public Task<Result> StageRestoreAsync(string backupFilePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }
}
