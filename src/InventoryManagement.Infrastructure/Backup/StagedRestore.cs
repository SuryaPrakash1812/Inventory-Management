using InventoryManagement.Infrastructure.Security;

namespace InventoryManagement.Infrastructure.Backup;

/// <summary>
/// Applies a staged restore, if one exists, BEFORE any database connection
/// is opened - this is what makes restore safe with this application's
/// single long-lived connection per session. BackupService.StageRestoreAsync
/// never touches the live database file while the app is running; it only
/// writes a ".pending-restore" file alongside it (already re-encrypted with
/// this PC's key). This runs at the very start of every launch, finds that
/// file if the user requested a restore last session, and only then swaps
/// it in.
///
/// The database being replaced is first copied aside as
/// "inventory.before-restore.db" (with its WAL sidecar files), so a restore
/// of the wrong backup can still be undone.
/// </summary>
public static class StagedRestore
{
    public const string PendingRestoreSuffix = ".pending-restore";

    /// <returns>True if a restore was applied.</returns>
    public static bool ApplyIfPending(string databasePath)
    {
        var stagedPath = databasePath + PendingRestoreSuffix;

        if (!File.Exists(stagedPath))
        {
            return false;
        }

        var safetyCopyPath = Path.Combine(
            Path.GetDirectoryName(databasePath)!,
            Path.GetFileNameWithoutExtension(databasePath) + ".before-restore" + Path.GetExtension(databasePath));

        DatabaseEncryption.DeleteDatabaseFiles(safetyCopyPath);

        // SQLite WAL mode leaves -wal/-shm sidecar files alongside the main
        // database file. They are carried over to the safety copy (so it is
        // complete) and must be gone from the live path afterwards, or the
        // restored file could be reconciled against stale WAL data from
        // before the restore, defeating the point of restoring.
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = databasePath + suffix;
            if (File.Exists(path))
            {
                File.Move(path, safetyCopyPath + suffix);
            }
        }

        File.Move(stagedPath, databasePath);
        return true;
    }
}
