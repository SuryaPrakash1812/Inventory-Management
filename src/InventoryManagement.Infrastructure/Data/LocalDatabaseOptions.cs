using Microsoft.Data.Sqlite;

namespace InventoryManagement.Infrastructure.Data;

/// <summary>
/// Where the local SQLite database is and the key that opens it. Built once
/// at startup (see App.xaml.cs and LocalDatabaseBootstrapper) and shared by
/// every place that opens the database, so none of them can disagree about
/// the connection string - previously three places each built their own.
///
/// A null <see cref="Password"/> means an unencrypted database; only tests
/// and EF's design-time tooling use that.
/// </summary>
public sealed record LocalDatabaseOptions(string DatabasePath, string? Password)
{
    public string ConnectionString => BuildConnectionString(DatabasePath, Password);

    public static string BuildConnectionString(string databasePath, string? password, bool pooling = true)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = pooling,
        };

        if (!string.IsNullOrEmpty(password))
        {
            // Microsoft.Data.Sqlite issues "PRAGMA key" with this value on
            // every new connection; with the SQLCipher native library that
            // is what decrypts the file.
            builder.Password = password;
        }

        return builder.ToString();
    }
}
