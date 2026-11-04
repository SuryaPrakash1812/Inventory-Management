using System.Text;
using InventoryManagement.Infrastructure.Data;
using Microsoft.Data.Sqlite;

namespace InventoryManagement.Infrastructure.Security;

/// <summary>
/// SQLCipher file-level operations shared by startup (encrypting a database
/// created before encryption existed), backup (writing a copy encrypted with
/// the customer's backup key) and restore (re-encrypting a backup with this
/// PC's database key).
///
/// All of them use SQLCipher's documented sqlcipher_export: ATTACH the
/// target file with its own key, export the whole schema + data into it,
/// DETACH. Unlike copying the file, this works while the source is open and
/// changes the key in one step.
/// </summary>
public static class DatabaseEncryption
{
    private static readonly byte[] PlaintextHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");

    /// <summary>True if the file is an ordinary, unencrypted SQLite database (an encrypted one has no readable header).</summary>
    public static bool IsPlaintextDatabase(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var header = new byte[PlaintextHeader.Length];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return stream.Read(header, 0, header.Length) == header.Length && header.AsSpan().SequenceEqual(PlaintextHeader);
    }

    /// <summary>Opens the file with the given key and runs a trivial query - false if the key is wrong or the file is not a database.</summary>
    public static bool CanOpen(string path, string? password)
    {
        try
        {
            using var connection = Open(path, password, readOnly: true);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master;";
            command.ExecuteScalar();
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    public static SqliteConnection Open(string path, string? password, bool readOnly = false)
    {
        // The key is applied AFTER Open() rather than via the connection
        // string's Password keyword: when Password is wrong, Microsoft.Data.
        // Sqlite fails inside Open() and leaks the native handle, leaving the
        // file locked until a garbage collection. Opening first means a
        // wrong key fails on an OPEN connection, which Dispose() releases.
        var builder = new SqliteConnectionStringBuilder(LocalDatabaseOptions.BuildConnectionString(path, password: null, pooling: false))
        {
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
        };

        var connection = new SqliteConnection(builder.ToString());
        try
        {
            connection.Open();

            if (!string.IsNullOrEmpty(password))
            {
                using var command = connection.CreateCommand();

                // PRAGMA cannot take bound parameters, so the key is quoted
                // by SQLite itself rather than by string concatenation here.
                command.CommandText = "SELECT quote($key);";
                command.Parameters.AddWithValue("$key", password);
                var quotedKey = (string)command.ExecuteScalar()!;

                command.Parameters.Clear();
                command.CommandText = $"PRAGMA key = {quotedKey}; SELECT COUNT(*) FROM sqlite_master;";
                command.ExecuteScalar();
            }
        }
        catch
        {
            connection.Dispose();
            throw;
        }

        return connection;
    }

    /// <summary>
    /// Writes a complete copy of the database that <paramref name="source"/>
    /// is connected to into <paramref name="destinationPath"/>, encrypted
    /// with <paramref name="destinationPassword"/> (null/empty = plaintext).
    /// The destination must not already exist.
    /// </summary>
    public static async Task ExportAsync(
        SqliteConnection source, string destinationPath, string? destinationPassword, CancellationToken cancellationToken = default)
    {
        await using var command = source.CreateCommand();

        command.CommandText = "ATTACH DATABASE $path AS export_target KEY $key;";
        command.Parameters.AddWithValue("$path", destinationPath);
        command.Parameters.AddWithValue("$key", destinationPassword ?? string.Empty);
        await command.ExecuteNonQueryAsync(cancellationToken);

        try
        {
            command.Parameters.Clear();
            command.CommandText = "SELECT sqlcipher_export('export_target');";
            await command.ExecuteScalarAsync(cancellationToken);
        }
        finally
        {
            command.Parameters.Clear();
            command.CommandText = "DETACH DATABASE export_target;";
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Converts an unencrypted database file into an encrypted one, in place.
    /// Used once, on the first start after upgrading from a version that
    /// stored data unencrypted. The encrypted copy is fully written and
    /// verified before the original is removed, so a failure part-way leaves
    /// the original untouched.
    /// </summary>
    public static async Task EncryptInPlaceAsync(string databasePath, string password, CancellationToken cancellationToken = default)
    {
        var encryptedPath = databasePath + ".encrypting";
        DeleteDatabaseFiles(encryptedPath);

        await using (var plaintext = Open(databasePath, password: null))
        {
            await ExportAsync(plaintext, encryptedPath, password, cancellationToken);
        }

        if (!CanOpen(encryptedPath, password))
        {
            DeleteDatabaseFiles(encryptedPath);
            throw new InvalidOperationException("The encrypted copy of the database could not be verified; the original was left unchanged.");
        }

        EnableWriteAheadLog(encryptedPath, password);

        SqliteConnection.ClearAllPools();
        DeleteDatabaseFiles(databasePath);
        File.Move(encryptedPath, databasePath);
    }

    /// <summary>
    /// sqlcipher_export writes its target in SQLite's default rollback-journal
    /// mode. Files that become the LIVE database are switched back to WAL
    /// (which the app always uses - see SqliteConnectionInterceptor) so they
    /// are in exactly the state the app expects from the very first open.
    /// Closing the connection checkpoints, so no -wal/-shm files are left.
    /// </summary>
    public static void EnableWriteAheadLog(string path, string? password)
    {
        using var connection = Open(path, password);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL;";
        command.ExecuteScalar();
    }

    /// <summary>Deletes a database file and its WAL-mode sidecar files, if present.</summary>
    public static void DeleteDatabaseFiles(string databasePath)
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
        {
            var path = databasePath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
