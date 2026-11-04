using InventoryManagement.Domain.Entities;
using InventoryManagement.Infrastructure.Auth;
using InventoryManagement.Infrastructure.Backup;
using InventoryManagement.Infrastructure.Common;
using InventoryManagement.Infrastructure.Data;
using InventoryManagement.Infrastructure.Data.Interceptors;
using InventoryManagement.Infrastructure.Licensing;
using InventoryManagement.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InventoryManagement.Infrastructure.Tests.Backup;

/// <summary>
/// Runs against a real SQLCipher-ENCRYPTED database (migrated, not
/// EnsureCreated, so __EFMigrationsHistory exists exactly as in production).
/// </summary>
public class BackupServiceTests : IDisposable
{
    private const string DatabasePassword = "test-database-key";
    private const string CustomerBackupKey = "customer-a-backup-key";

    private readonly string _folder;
    private readonly LocalDatabaseOptions _database;
    private readonly InventoryDbContext _context;
    private readonly BackupService _sut;

    public BackupServiceTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), $"InventoryManagementBackupTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_folder);

        _database = new LocalDatabaseOptions(Path.Combine(_folder, "inventory.db"), DatabasePassword);
        _context = CreateContext(_database);
        _context.Database.Migrate();

        _sut = CreateService(_context, CustomerBackupKey);
    }

    public void Dispose()
    {
        _context.Dispose();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    private static InventoryDbContext CreateContext(LocalDatabaseOptions database) =>
        new(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite(database.ConnectionString)
            .AddInterceptors(
                new AuditableEntitySaveChangesInterceptor(new SystemDateTimeProvider()),
                new SqliteConnectionInterceptor())
            .Options);

    private BackupService CreateService(InventoryDbContext context, string backupKey) =>
        new(context,
            new AuditLogger(context, new SystemDateTimeProvider(), new CurrentUserSession()),
            new SystemDateTimeProvider(),
            new FakeBackupKeyProvider(backupKey),
            _database);

    private string BackupsFolder => Path.Combine(_folder, "Backups");

    [Fact]
    public async Task LiveDatabase_IsEncrypted_AndOnlyOpensWithItsKey()
    {
        Assert.False(DatabaseEncryption.IsPlaintextDatabase(_database.DatabasePath));
        Assert.True(DatabaseEncryption.CanOpen(_database.DatabasePath, DatabasePassword));
        Assert.False(DatabaseEncryption.CanOpen(_database.DatabasePath, null));
        Assert.False(DatabaseEncryption.CanOpen(_database.DatabasePath, "wrong-key"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task CreateBackupAsync_WritesFileEncryptedWithTheCustomerBackupKey()
    {
        _context.Categories.Add(new Category { Name = "Beverages" });
        await _context.SaveChangesAsync();

        var result = await _sut.CreateBackupAsync(BackupsFolder);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var path = result.Value.FilePath;
        Assert.True(File.Exists(path));
        Assert.False(DatabaseEncryption.IsPlaintextDatabase(path));
        Assert.True(DatabaseEncryption.CanOpen(path, CustomerBackupKey));
        Assert.False(DatabaseEncryption.CanOpen(path, DatabasePassword));
        Assert.False(DatabaseEncryption.CanOpen(path, null));

        await using var backup = DatabaseEncryption.Open(path, CustomerBackupKey, readOnly: true);
        await using var command = backup.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM \"Categories\" WHERE \"Name\" = 'Beverages';";
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task StageRestoreAsync_OwnBackup_IsStagedReEncryptedWithThisPcsDatabaseKey()
    {
        var backup = await _sut.CreateBackupAsync(BackupsFolder);

        var result = await _sut.StageRestoreAsync(backup.Value.FilePath);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var staged = _database.DatabasePath + StagedRestore.PendingRestoreSuffix;
        Assert.True(DatabaseEncryption.CanOpen(staged, DatabasePassword));
        Assert.False(DatabaseEncryption.CanOpen(staged, CustomerBackupKey));
    }

    [Fact]
    public async Task StageRestoreAsync_BackupFromAnotherCustomer_IsRejected()
    {
        var otherCustomer = CreateService(_context, "customer-b-backup-key");
        var foreignBackup = await otherCustomer.CreateBackupAsync(BackupsFolder);

        var result = await _sut.StageRestoreAsync(foreignBackup.Value.FilePath);

        Assert.True(result.IsFailure);
        Assert.Contains("different license", result.Error);
        Assert.False(File.Exists(_database.DatabasePath + StagedRestore.PendingRestoreSuffix));
    }

    [Fact]
    public async Task StageRestoreAsync_BackupFromNewerAppVersion_IsRejected()
    {
        var backup = await _sut.CreateBackupAsync(BackupsFolder);
        await using (var connection = DatabaseEncryption.Open(backup.Value.FilePath, CustomerBackupKey))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('29991231000000_FromTheFuture', '8.0.8');";
            await command.ExecuteNonQueryAsync();
        }

        var result = await _sut.StageRestoreAsync(backup.Value.FilePath);

        Assert.True(result.IsFailure);
        Assert.Contains("newer version", result.Error);
    }

    [Fact]
    public async Task StageRestoreAsync_ArbitrarySqliteFile_IsRejected()
    {
        var path = Path.Combine(_folder, "not-ours.db");
        await using (var connection = DatabaseEncryption.Open(path, null))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE Something (Id INTEGER);";
            await command.ExecuteNonQueryAsync();
        }

        var result = await _sut.StageRestoreAsync(path);

        Assert.True(result.IsFailure);
        Assert.Contains("not an Inventory Management backup", result.Error);
    }

    [Fact]
    public async Task PruneAutomaticBackupsAsync_KeepsNewestAutomatic_AndNeverTouchesManual()
    {
        var manual = await _sut.CreateBackupAsync(BackupsFolder);
        var automaticPaths = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var automatic = await _sut.CreateBackupAsync(Path.Combine(BackupsFolder, $"run{i}"), isAutomatic: true);
            automaticPaths.Add(automatic.Value.FilePath);
        }

        var removed = await _sut.PruneAutomaticBackupsAsync(keepCount: 1);

        Assert.Equal(2, removed);
        Assert.True(File.Exists(manual.Value.FilePath));
        Assert.Equal(1, automaticPaths.Count(File.Exists));
        Assert.Equal(2, (await _sut.GetBackupHistoryAsync()).Count);
    }

    [Fact]
    public async Task StagedRestore_ApplyIfPending_SwapsFileAndKeepsSafetyCopy()
    {
        var folder = Path.Combine(_folder, "swap");
        Directory.CreateDirectory(folder);
        var live = Path.Combine(folder, "inventory.db");
        await File.WriteAllTextAsync(live, "current");
        await File.WriteAllTextAsync(live + StagedRestore.PendingRestoreSuffix, "restored");

        var applied = StagedRestore.ApplyIfPending(live);

        Assert.True(applied);
        Assert.Equal("restored", await File.ReadAllTextAsync(live));
        Assert.Equal("current", await File.ReadAllTextAsync(Path.Combine(folder, "inventory.before-restore.db")));
        Assert.False(File.Exists(live + StagedRestore.PendingRestoreSuffix));
        Assert.False(StagedRestore.ApplyIfPending(live));
    }

    [Fact]
    public async Task EncryptInPlaceAsync_ConvertsPlaintextDatabase_KeepingItsData()
    {
        var path = Path.Combine(_folder, "legacy.db");
        await using (var connection = DatabaseEncryption.Open(path, null))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE Items (Name TEXT); INSERT INTO Items VALUES ('kept');";
            await command.ExecuteNonQueryAsync();
        }

        Assert.True(DatabaseEncryption.IsPlaintextDatabase(path));

        await DatabaseEncryption.EncryptInPlaceAsync(path, "new-key");

        Assert.False(DatabaseEncryption.IsPlaintextDatabase(path));
        await using var encrypted = DatabaseEncryption.Open(path, "new-key", readOnly: true);
        await using var query = encrypted.CreateCommand();
        query.CommandText = "SELECT Name FROM Items;";
        Assert.Equal("kept", (string)(await query.ExecuteScalarAsync())!);
    }

    /// <summary>
    /// The real upgrade path: a database created by the old (unencrypted)
    /// app - in WAL mode, via the same interceptor - is encrypted in place,
    /// then reopened exactly the way the app does it (EF + Password +
    /// SqliteConnectionInterceptor) and migrated and written to.
    /// </summary>
    [Fact]
    public async Task UpgradedPlaintextDatabase_OpensAndWritesThroughEf_AfterEncryptInPlace()
    {
        var legacy = new LocalDatabaseOptions(Path.Combine(_folder, "upgrade.db"), null);
        await using (var oldApp = CreateContext(legacy))
        {
            await oldApp.Database.MigrateAsync();
            oldApp.Categories.Add(new Category { Name = "From old version" });
            await oldApp.SaveChangesAsync();
        }

        SqliteConnection.ClearAllPools();
        Assert.True(DatabaseEncryption.IsPlaintextDatabase(legacy.DatabasePath));

        await DatabaseEncryption.EncryptInPlaceAsync(legacy.DatabasePath, "upgrade-key");

        var upgraded = new LocalDatabaseOptions(legacy.DatabasePath, "upgrade-key");
        await using var newApp = CreateContext(upgraded);
        await newApp.Database.MigrateAsync();
        newApp.Categories.Add(new Category { Name = "From new version" });
        await newApp.SaveChangesAsync();

        Assert.Equal(2, await newApp.Categories.CountAsync());
    }

    /// <summary>
    /// Regression: a database in rollback-journal mode (as left by an
    /// earlier build's encrypt-in-place) failed on startup with "attempt to
    /// write a readonly database", because EF's read-only existence check
    /// ran the interceptor's journal_mode=WAL switch.
    /// </summary>
    [Fact]
    public async Task EncryptedDatabaseInRollbackJournalMode_StillOpensAndMigrates()
    {
        var path = Path.Combine(_folder, "rollback.db");
        await using (var connection = DatabaseEncryption.Open(path, "rollback-key"))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA journal_mode = DELETE;";
            await command.ExecuteScalarAsync();
        }

        await using var context = CreateContext(new LocalDatabaseOptions(path, "rollback-key"));
        await context.Database.MigrateAsync();
        context.Categories.Add(new Category { Name = "Works" });
        await context.SaveChangesAsync();

        Assert.Equal(1, await context.Categories.CountAsync());
    }

    private sealed class FakeBackupKeyProvider(string password) : IBackupKeyProvider
    {
        public string BackupPassword { get; } = password;
    }
}
