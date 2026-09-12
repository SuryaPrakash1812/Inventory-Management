using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace InventoryManagement.Infrastructure.Data.Interceptors;

/// <summary>
/// SQLite enforces neither foreign keys nor its faster write-ahead-log
/// journal mode by default - both are per-connection PRAGMA settings, not
/// database-wide ones, so they must be (re)applied every time a connection
/// is opened. This is exactly what "connection management" means for
/// SQLite: without this, foreign key violations would silently succeed.
/// </summary>
public sealed class SqliteConnectionInterceptor : DbConnectionInterceptor
{
    private const string PragmaSql = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;";

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

    private static void ApplyPragmas(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = PragmaSql;
        command.ExecuteNonQuery();
    }

    private static async Task ApplyPragmasAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = PragmaSql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
