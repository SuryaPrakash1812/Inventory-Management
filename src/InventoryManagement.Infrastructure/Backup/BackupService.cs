using InventoryManagement.Application.Auth;
using InventoryManagement.Application.Backup;
using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Core.Common;
using InventoryManagement.Domain.Entities;
using InventoryManagement.Domain.Enums;
using InventoryManagement.Infrastructure.Common;
using InventoryManagement.Infrastructure.Data;
using InventoryManagement.Infrastructure.Licensing;
using InventoryManagement.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Infrastructure.Backup;

public sealed class BackupService : IBackupService
{
    private readonly IAppDbContext _context;
    private readonly IAuditLogger _auditLogger;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IBackupKeyProvider _backupKeyProvider;
    private readonly LocalDatabaseOptions _database;

    public BackupService(
        IAppDbContext context,
        IAuditLogger auditLogger,
        IDateTimeProvider dateTimeProvider,
        IBackupKeyProvider backupKeyProvider,
        LocalDatabaseOptions database)
    {
        _context = context;
        _auditLogger = auditLogger;
        _dateTimeProvider = dateTimeProvider;
        _backupKeyProvider = backupKeyProvider;
        _database = database;
    }

    public async Task<IReadOnlyList<BackupRecordSummary>> GetBackupHistoryAsync(CancellationToken cancellationToken = default)
    {
        var records = await _context.BackupRecords.AsNoTracking().ToListAsync(cancellationToken);

        return records
            .OrderByDescending(r => r.CreatedAtUtc)
            .Select(ToSummary)
            .ToList();
    }

    public async Task<Result<BackupRecordSummary>> CreateBackupAsync(
        string? destinationFolder = null, bool isAutomatic = false, CancellationToken cancellationToken = default)
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

        var kind = isAutomatic ? "auto" : "manual";
        var fileName = $"inventory-backup-{_dateTimeProvider.UtcNow:yyyyMMdd-HHmmss}-{kind}.imbak";
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
            IsAutomatic = isAutomatic,
        };

        try
        {
            // Exported on the context's own connection, so the backup is a
            // consistent snapshot of exactly what this session has
            // committed, even while the app is in use.
            await _context.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                var connection = (SqliteConnection)_context.Database.GetDbConnection();
                await DatabaseEncryption.ExportAsync(connection, filePath, _backupKeyProvider.BackupPassword, cancellationToken);
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }

            if (!DatabaseEncryption.CanOpen(filePath, _backupKeyProvider.BackupPassword))
            {
                throw new InvalidOperationException("The backup file was written but could not be read back for verification.");
            }

            record.SizeBytes = new FileInfo(filePath).Length;
            record.Status = BackupStatus.Success;
            record.VerifiedAtUtc = _dateTimeProvider.UtcNow;
        }
        catch (Exception ex)
        {
            record.Status = BackupStatus.Failed;
            record.Notes = ex.Message;
            TryDelete(filePath);
        }

        _context.BackupRecords.Add(record);

        await _auditLogger.LogAsync(
            AuditAction.Created, nameof(BackupRecord), record.Id,
            record.Status == BackupStatus.Success
                ? $"{(isAutomatic ? "Automatic" : "Manual")} backup '{record.FileName}' created successfully."
                : $"Backup attempt failed: {record.Notes}",
            cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return record.Status == BackupStatus.Success
            ? Result.Success(ToSummary(record))
            : Result.Failure<BackupRecordSummary>($"Backup failed: {record.Notes}");
    }

    public async Task<DateTimeOffset?> GetLastAutomaticBackupTimeAsync(CancellationToken cancellationToken = default)
    {
        // DateTimeOffset ordering is not translatable by the SQLite provider,
        // so this sorts client-side (as GetBackupHistoryAsync does); the
        // backup history is small.
        var times = await _context.BackupRecords.AsNoTracking()
            .Where(r => r.IsAutomatic && r.Status == BackupStatus.Success)
            .Select(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return times.Count == 0 ? null : times.Max();
    }

    public async Task<int> PruneAutomaticBackupsAsync(int keepCount, CancellationToken cancellationToken = default)
    {
        keepCount = Math.Max(1, keepCount);

        var automatic = (await _context.BackupRecords
                .Where(r => r.IsAutomatic && r.Status == BackupStatus.Success)
                .ToListAsync(cancellationToken))
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip(keepCount)
            .ToList();

        foreach (var record in automatic)
        {
            TryDelete(record.FilePath);
            _context.BackupRecords.Remove(record);
        }

        if (automatic.Count > 0)
        {
            await _auditLogger.LogAsync(
                AuditAction.Deleted, nameof(BackupRecord), null,
                $"Removed {automatic.Count} old automatic backup(s) - retention keeps the newest {keepCount}.",
                cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return automatic.Count;
    }

    public async Task<Result> StageRestoreAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupFilePath))
        {
            return Result.Failure("The selected backup file does not exist.");
        }

        // Legacy backups (taken before encryption existed) are plain SQLite
        // files; everything newer must open with this customer's key.
        string? sourcePassword = DatabaseEncryption.IsPlaintextDatabase(backupFilePath)
            ? null
            : _backupKeyProvider.BackupPassword;

        if (!DatabaseEncryption.CanOpen(backupFilePath, sourcePassword))
        {
            return Result.Failure(
                "This backup could not be opened. It was created under a different license, or the file is damaged.");
        }

        var stagedPath = _database.DatabasePath + StagedRestore.PendingRestoreSuffix;

        try
        {
            // Not read-only: SQLite applies a read-only open to ATTACHed
            // databases too, which would stop the export from creating the
            // staged file. Nothing below writes to the backup itself.
            await using var source = DatabaseEncryption.Open(backupFilePath, sourcePassword);

            var validationError = await ValidateBackupSchemaAsync(source, cancellationToken);
            if (validationError is not null)
            {
                return Result.Failure(validationError);
            }

            // Re-encrypted with THIS PC's database key, so the file that is
            // swapped in at next startup opens exactly like the current one.
            DatabaseEncryption.DeleteDatabaseFiles(stagedPath);
            await DatabaseEncryption.ExportAsync(source, stagedPath, _database.Password, cancellationToken);
            DatabaseEncryption.EnableWriteAheadLog(stagedPath, _database.Password);
        }
        catch (Exception ex)
        {
            DatabaseEncryption.DeleteDatabaseFiles(stagedPath);
            return Result.Failure($"Could not stage the restore: {ex.Message}");
        }

        await _auditLogger.LogAsync(
            AuditAction.Updated, nameof(BackupRecord), Guid.Empty,
            $"Restore staged from '{Path.GetFileName(backupFilePath)}' - will apply on next application restart.",
            cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// A backup is only restorable if it is this application's database, at
    /// a schema version this build understands: every migration it has
    /// applied must be one this build knows. A backup from a NEWER version
    /// would otherwise be swapped in and then fail (or be damaged) when this
    /// older build tried to use it.
    /// </summary>
    private async Task<string?> ValidateBackupSchemaAsync(SqliteConnection source, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('__EFMigrationsHistory', 'Products', 'Users');";
        var requiredTables = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);

        if (requiredTables < 3)
        {
            return "The selected file is not an Inventory Management backup.";
        }

        command.CommandText = "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\";";
        var applied = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                applied.Add(reader.GetString(0));
            }
        }

        var known = _context.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
        if (applied.Any(migration => !known.Contains(migration)))
        {
            return "This backup was created by a newer version of Inventory Management. Update the application, then restore it.";
        }

        return null;
    }

    private static BackupRecordSummary ToSummary(BackupRecord r) =>
        new(r.Id, r.CreatedAtUtc, r.FileName, r.FilePath, r.SizeBytes, r.Status, r.IsAutomatic, r.Notes);

    private static void TryDelete(string path)
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A file that can't be deleted right now (e.g. open in another
            // program) is left behind rather than failing the whole operation.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
