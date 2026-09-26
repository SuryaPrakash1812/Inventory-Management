using InventoryManagement.Application.Auth;
using InventoryManagement.Application.Backup;
using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Core.Common;
using InventoryManagement.Domain.Entities;
using InventoryManagement.Domain.Enums;
using InventoryManagement.Infrastructure.Common;
using InventoryManagement.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Infrastructure.Backup;

public sealed class BackupService : IBackupService
{
    /// <summary>Suffix marking a staged restore file - checked by App.xaml.cs at startup, before any database connection is opened.</summary>
    public const string PendingRestoreSuffix = ".pending-restore";

    private readonly IAppDbContext _context;
    private readonly IAuditLogger _auditLogger;
    private readonly IDateTimeProvider _dateTimeProvider;

    public BackupService(IAppDbContext context, IAuditLogger auditLogger, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _auditLogger = auditLogger;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<IReadOnlyList<BackupRecordSummary>> GetBackupHistoryAsync(CancellationToken cancellationToken = default)
    {
        var records = await _context.BackupRecords.AsNoTracking().ToListAsync(cancellationToken);

        return records
            .OrderByDescending(r => r.CreatedAtUtc)
            .Select(r => new BackupRecordSummary(r.Id, r.CreatedAtUtc, r.FileName, r.FilePath, r.SizeBytes, r.Status, r.Notes))
            .ToList();
    }

    public async Task<Result<BackupRecordSummary>> CreateBackupAsync(
        string? destinationFolder = null, CancellationToken cancellationToken = default)
    {
        var folder = string.IsNullOrWhiteSpace(destinationFolder) ? AppPaths.BackupsFolder : destinationFolder;

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            return Result.Failure<BackupRecordSummary>($"Could not access the backup destination: {ex.Message}");
        }

        var fileName = $"inventory-backup-{_dateTimeProvider.UtcNow:yyyyMMdd-HHmmss}.db";
        var filePath = Path.Combine(folder, fileName);

        if (File.Exists(filePath))
        {
            return Result.Failure<BackupRecordSummary>(
                $"A backup file named '{fileName}' already exists at that destination. Try again in a moment.");
        }

        var record = new BackupRecord
        {
            CreatedAtUtc = _dateTimeProvider.UtcNow,
            FileName = fileName,
            FilePath = filePath,
            Destination = BackupDestination.Local,
            Status = BackupStatus.InProgress,
            IsAutomatic = false,
        };

        try
        {
            var escapedPath = filePath.Replace("'", "''");
            await _context.Database.ExecuteSqlRawAsync($"VACUUM INTO '{escapedPath}';", cancellationToken);

            record.SizeBytes = new FileInfo(filePath).Length;
            record.Status = BackupStatus.Success;
            record.VerifiedAtUtc = _dateTimeProvider.UtcNow;
        }
        catch (Exception ex)
        {
            record.Status = BackupStatus.Failed;
            record.Notes = ex.Message;
        }

        _context.BackupRecords.Add(record);

        await _auditLogger.LogAsync(
            AuditAction.Created, nameof(BackupRecord), record.Id,
            record.Status == BackupStatus.Success
                ? $"Backup '{record.FileName}' created successfully."
                : $"Backup attempt failed: {record.Notes}",
            cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return record.Status == BackupStatus.Success
            ? Result.Success(new BackupRecordSummary(
                record.Id, record.CreatedAtUtc, record.FileName, record.FilePath, record.SizeBytes, record.Status, record.Notes))
            : Result.Failure<BackupRecordSummary>($"Backup failed: {record.Notes}");
    }

    public async Task<Result> StageRestoreAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupFilePath))
        {
            return Result.Failure("The selected backup file does not exist.");
        }

        try
        {
            await using var testConnection = new SqliteConnection($"Data Source={backupFilePath};Mode=ReadOnly");
            await testConnection.OpenAsync(cancellationToken);

            await using var command = testConnection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table';";
            var tableCount = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);

            if (tableCount == 0)
            {
                return Result.Failure("The selected file does not look like a valid Inventory Management backup (no tables found).");
            }
        }
        catch (Exception ex)
        {
            return Result.Failure($"The selected file could not be opened as a SQLite database: {ex.Message}");
        }

        var stagedPath = AppPaths.DatabaseFilePath + PendingRestoreSuffix;

        try
        {
            File.Copy(backupFilePath, stagedPath, overwrite: true);
        }
        catch (Exception ex)
        {
            return Result.Failure($"Could not stage the restore: {ex.Message}");
        }

        await _auditLogger.LogAsync(
            AuditAction.Updated, nameof(BackupRecord), Guid.Empty,
            $"Restore staged from '{Path.GetFileName(backupFilePath)}' - will apply on next application restart.",
            cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
