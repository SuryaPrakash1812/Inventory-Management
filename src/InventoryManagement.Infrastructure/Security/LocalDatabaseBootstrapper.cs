using System.Runtime.Versioning;
using InventoryManagement.Infrastructure.Data;

namespace InventoryManagement.Infrastructure.Security;

/// <summary>
/// Runs before anything opens the database: gets this PC's database key and
/// makes sure the file on disk is encrypted with it. Covers the three states
/// a PC can be in:
///
/// 1. Fresh install - no database, no key: a key is created; the database is
///    created (encrypted) by the normal migration step afterwards.
/// 2. Upgrade from an unencrypted version - plaintext database, no key: a key
///    is created and the existing data is encrypted in place.
/// 3. Normal start - key and encrypted database both present.
///
/// An encrypted database with no usable key (deleted key file, or files
/// copied from another PC) cannot be opened by anyone; that is reported as
/// <see cref="DatabaseKeyUnavailableException"/> so the app can tell the user
/// to restore a backup, instead of silently starting with an empty database.
/// </summary>
[SupportedOSPlatform("windows")]
public static class LocalDatabaseBootstrapper
{
    public static async Task<LocalDatabaseOptions> PrepareAsync(
        string databasePath, DatabaseKeyProvider keyProvider, CancellationToken cancellationToken = default)
    {
        var databaseExists = File.Exists(databasePath);
        var isPlaintext = DatabaseEncryption.IsPlaintextDatabase(databasePath);

        if (databaseExists && !isPlaintext && !keyProvider.KeyExists)
        {
            throw new DatabaseKeyUnavailableException(
                "The database is encrypted, but its key is missing from this PC.");
        }

        var password = keyProvider.GetOrCreatePassword();

        if (isPlaintext)
        {
            await DatabaseEncryption.EncryptInPlaceAsync(databasePath, password, cancellationToken);
        }
        else if (databaseExists && !DatabaseEncryption.CanOpen(databasePath, password))
        {
            throw new DatabaseKeyUnavailableException(
                "The database on this PC could not be unlocked with this PC's key. It appears to have been copied from another PC.");
        }

        return new LocalDatabaseOptions(databasePath, password);
    }
}
