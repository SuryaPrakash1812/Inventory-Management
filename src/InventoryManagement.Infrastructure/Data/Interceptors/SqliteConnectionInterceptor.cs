using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace InventoryManagement.Infrastructure.Data.Interceptors;

/// <summary>
/// SQLite enforces neither foreign keys nor its faster write-ahead-log
/// journal mode by default - both are per-connection PRAGMA settings, not
/// database-wide ones, so they must be (re)applied every time a connection
/// is opened. This is exactly what "connection management" means for
/// SQLite: without this, foreign key violations would silently succeed.
///
/// busy_timeout matters specifically for concurrent stock-changing
/// operations (see InventoryService): SQLite only ever allows one writer
/// across the whole database at a time. Without a busy_timeout, a second
/// writer that arrives while another write transaction is still open fails
/// immediately with SQLITE_BUSY; with it, SQLite retries internally for up
/// to this many milliseconds before giving up, so a second concurrent
/// stock update simply waits its turn instead of erroring out.
///
/// journal_mode is skipped on READ-ONLY connections: EF Core opens one
/// (Mode=ReadOnly) to check whether the database exists before migrating.
/// Switching a file that is not yet in WAL mode is a write, which a
/// read-only connection refuses ("attempt to write a readonly database") -
/// that is exactly what happened on the first start after a database was
/// encrypted, because SQLCipher's export writes a rollback-journal file. The
/// next read-write connection switches it to WAL as usual.
/// </summary>
public sealed class SqliteConnectionInterceptor : DbConnectionInterceptor
{
    private const string PragmaSql =
        "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000;";

    private const string ReadOnlyPragmaSql =
        "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyPragmas(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplyPragmasAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private static string PragmasFor(DbConnection connection) =>
        new SqliteConnectionStringBuilder(connection.ConnectionString).Mode == SqliteOpenMode.ReadOnly
            ? ReadOnlyPragmaSql
            : PragmaSql;

    private static void ApplyPragmas(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = PragmasFor(connection);
        command.ExecuteNonQuery();
    }

    private static async Task ApplyPragmasAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = PragmasFor(connection);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
