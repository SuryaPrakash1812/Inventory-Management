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
    bool IsAutomatic,
    string? Notes);

public interface IBackupService
{
    Task<IReadOnlyList<BackupRecordSummary>> GetBackupHistoryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a consistent backup of the current database - safe to run
    /// while the application (and its long-lived database connection) is
    /// actively open, unlike a plain file copy. The backup is ENCRYPTED with
    /// the customer's license backup key: it can only be restored by this
    /// customer's copy of the application (on this PC, or on a replacement
    /// PC licensed to the same customer). destinationFolder defaults to
    /// AppPaths.BackupsFolder if null.
    /// </summary>
    Task<Result<BackupRecordSummary>> CreateBackupAsync(
        string? destinationFolder = null, bool isAutomatic = false, CancellationToken cancellationToken = default);

    /// <summary>When the most recent successful automatic backup was taken, or null if there has never been one.</summary>
    Task<DateTimeOffset?> GetLastAutomaticBackupTimeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the files (and history rows) of all but the newest
    /// <paramref name="keepCount"/> successful AUTOMATIC backups. Manual
    /// backups are never touched. Returns how many were removed.
    /// </summary>
    Task<int> PruneAutomaticBackupsAsync(int keepCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the selected backup file - it must open with this
    /// customer's backup key (or be a legacy unencrypted backup), contain
    /// this application's schema, and not come from a NEWER version of the
    /// application - and, if valid, STAGES it for restore. This does NOT
    /// modify the live database while the application is running -
    /// overwriting an actively-open SQLite file is unsafe with this
    /// application's single long-lived connection per session. The actual
    /// restore (safety-copying the current database, then replacing it with
    /// the staged file) happens automatically the next time the application
    /// starts, before any database connection is opened - see
    /// StagedRestore.ApplyIfPending. The UI must tell the user a restart is
    /// required.
    /// </summary>
    Task<Result> StageRestoreAsync(string backupFilePath, CancellationToken cancellationToken = default);
}
