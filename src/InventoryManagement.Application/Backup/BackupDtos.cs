using InventoryManagement.Core.Common;
using InventoryManagement.Domain.Enums;

namespace InventoryManagement.Application.Backup;

public sealed record BackupRecordSummary(
    Guid Id,
    DateTimeOffset CreatedAtUtc,
    string FileName,
    string FilePath,
    long SizeBytes,
    BackupStatus Status,
    string? Notes);

public interface IBackupService
{
    Task<IReadOnlyList<BackupRecordSummary>> GetBackupHistoryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a consistent local backup of the current database via
    /// SQLite's VACUUM INTO - safe to run while the application (and its
    /// long-lived database connection) is actively open, unlike a plain
    /// file copy. destinationFolder defaults to AppPaths.BackupsFolder if
    /// null.
    /// </summary>
    Task<Result<BackupRecordSummary>> CreateBackupAsync(string? destinationFolder = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the selected backup file (confirms it is a genuine,
    /// readable SQLite database - not merely that a file exists at the
    /// path) and, if valid, STAGES it for restore. This does NOT modify
    /// the live database while the application is running - overwriting
    /// an actively-open SQLite file is unsafe with this application's
    /// single long-lived connection per session. The actual restore
    /// (safety-backing-up the current database, then replacing it with
    /// the staged file) happens automatically the next time the
    /// application starts, before any database connection is opened -
    /// see App.xaml.cs's startup sequence. The returned result's message
    /// makes this explicit so the UI can tell the user a restart is
    /// required.
    /// </summary>
    Task<Result> StageRestoreAsync(string backupFilePath, CancellationToken cancellationToken = default);
}
