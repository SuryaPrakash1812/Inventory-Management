using InventoryManagement.Domain.Entities;
using InventoryManagement.Domain.Enums;
using InventoryManagement.Infrastructure.Common;
using InventoryManagement.Infrastructure.Data;
using InventoryManagement.Infrastructure.Data.Interceptors;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InventoryManagement.Sync.Tests.Outbox;

/// <summary>
/// Direct coverage for OutboxProcessor against a REAL SQLite file (not
/// InMemory) - this is deliberate. The bug this class exists to catch
/// (ORDER BY on a DateTimeOffset column - SQLite's EF Core provider cannot
/// translate that into SQL) only manifests against the real relational
/// provider; EF Core's InMemory provider would silently sort correctly
/// and never reveal it. This exact gap - zero test coverage for
/// OutboxProcessor - is what let that bug sit undetected in real
/// application code until a symptom in unrelated test code led to
/// checking for it by inspection.
/// </summary>
public sealed class OutboxProcessorTests : IDisposable
{
    private readonly string _databaseFilePath;
    private readonly FakeSyncDbContextFactory _factory;
    private readonly InventoryManagement.Sync.Outbox.OutboxProcessor _sut;

    public OutboxProcessorTests()
    {
        _databaseFilePath = Path.Combine(Path.GetTempPath(), $"InventoryManagementOutboxTests_{Guid.NewGuid()}.db");
        _factory = new FakeSyncDbContextFactory(_databaseFilePath);

        using var setup = _factory.CreateDbContext();
        setup.Database.EnsureCreated();

        _sut = new InventoryManagement.Sync.Outbox.OutboxProcessor(_factory);
    }

    private OutboxOperation MakePending(DateTimeOffset createdAtUtc, DateTimeOffset? lastAttemptAtUtc = null, int retryCount = 0)
    {
        var operation = new OutboxOperation
        {
            OperationType = "Purchase.Create",
            EntityType = nameof(Purchase),
            EntityId = Guid.NewGuid(),
            PayloadJson = "{}",
            CreatedAtUtc = createdAtUtc,
            LastAttemptAtUtc = lastAttemptAtUtc,
            RetryCount = retryCount,
            Status = OutboxOperationStatus.Pending,
        };

        using var context = _factory.CreateDbContext();
        context.OutboxOperations.Add(operation);
        context.SaveChanges();

        return operation;
    }

    [Fact]
    public async Task GetPendingOperationsAsync_DoesNotThrow_AndReturnsOrderedByCreatedAtUtc()
    {
        // Regression test for the exact bug fixed: OrderBy(o =>
        // o.CreatedAtUtc) against SQLite must not throw
        // NotSupportedException, and the eventual ordering must still be
        // correct (oldest first) even though it now happens in memory.
        var newest = MakePending(DateTimeOffset.UtcNow);
        var oldest = MakePending(DateTimeOffset.UtcNow.AddMinutes(-10));
        var middle = MakePending(DateTimeOffset.UtcNow.AddMinutes(-5));

        var pending = await _sut.GetPendingOperationsAsync();

        Assert.Equal(3, pending.Count);
        Assert.Equal(oldest.Id, pending[0].Id);
        Assert.Equal(middle.Id, pending[1].Id);
        Assert.Equal(newest.Id, pending[2].Id);
    }

    [Fact]
    public async Task GetPendingOperationsAsync_ExcludesNonPendingOperations()
    {
        MakePending(DateTimeOffset.UtcNow);
        var syncedId = MakePending(DateTimeOffset.UtcNow).Id;
        await _sut.MarkSyncedAsync(syncedId);

        var pending = await _sut.GetPendingOperationsAsync();

        Assert.DoesNotContain(pending, o => o.Id == syncedId);
    }

    [Fact]
    public async Task GetPendingOperationsAsync_ExcludesOperationsStillWithinBackoffWindow()
    {
        // RetryCount 1 => backoff of 30s (see OutboxProcessor.BackoffFor) -
        // an attempt 5 seconds ago is still within that window.
        var recentlyFailed = MakePending(
            DateTimeOffset.UtcNow.AddHours(-1), lastAttemptAtUtc: DateTimeOffset.UtcNow.AddSeconds(-5), retryCount: 1);

        var pending = await _sut.GetPendingOperationsAsync();

        Assert.DoesNotContain(pending, o => o.Id == recentlyFailed.Id);
    }

    [Fact]
    public async Task GetPendingOperationsAsync_IncludesOperationsPastTheirBackoffWindow()
    {
        var longAgoFailed = MakePending(
            DateTimeOffset.UtcNow.AddHours(-1), lastAttemptAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5), retryCount: 1);

        var pending = await _sut.GetPendingOperationsAsync();

        Assert.Contains(pending, o => o.Id == longAgoFailed.Id);
    }

    [Fact]
    public async Task GetPendingOperationsAsync_NeverHoldsBackAFirstAttempt()
    {
        // RetryCount 0, LastAttemptAtUtc null - never subject to backoff,
        // regardless of how "recently" it was created.
        var freshOperation = MakePending(DateTimeOffset.UtcNow);

        var pending = await _sut.GetPendingOperationsAsync();

        Assert.Contains(pending, o => o.Id == freshOperation.Id);
    }

    [Fact]
    public async Task MarkSyncedAsync_SetsStatusToSynced_AndItDisappearsFromPending()
    {
        var operation = MakePending(DateTimeOffset.UtcNow);

        await _sut.MarkSyncedAsync(operation.Id);

        using var context = _factory.CreateDbContext();
        var reloaded = await context.OutboxOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(OutboxOperationStatus.Synced, reloaded.Status);

        var pending = await _sut.GetPendingOperationsAsync();
        Assert.DoesNotContain(pending, o => o.Id == operation.Id);
    }

    [Fact]
    public async Task MarkFailedAsync_SetsStatusToFailed_TerminalAndNeverPendingAgain()
    {
        var operation = MakePending(DateTimeOffset.UtcNow);

        await _sut.MarkFailedAsync(operation.Id, "Server rejected this permanently.");

        using var context = _factory.CreateDbContext();
        var reloaded = await context.OutboxOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(OutboxOperationStatus.Failed, reloaded.Status);
        Assert.Equal("Server rejected this permanently.", reloaded.ErrorMessage);
        Assert.Equal(1, reloaded.RetryCount);

        var pending = await _sut.GetPendingOperationsAsync();
        Assert.DoesNotContain(pending, o => o.Id == operation.Id);
    }

    [Fact]
    public async Task MarkTransientFailureAsync_ReturnsToPending_WithIncrementedRetryCountAndErrorMessage()
    {
        var operation = MakePending(DateTimeOffset.UtcNow.AddHours(-1));

        await _sut.MarkTransientFailureAsync(operation.Id, "Network timeout.");

        using var context = _factory.CreateDbContext();
        var reloaded = await context.OutboxOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(OutboxOperationStatus.Pending, reloaded.Status);
        Assert.Equal(1, reloaded.RetryCount);
        Assert.Equal("Network timeout.", reloaded.ErrorMessage);
        Assert.NotNull(reloaded.LastAttemptAtUtc);
    }

    [Fact]
    public async Task ApplyServerPurchaseNumberAsync_UpdatesTheLocalPurchaseNumber()
    {
        var supplier = new Supplier { Name = "Acme" };
        var purchase = new Purchase
        {
            PurchaseNumber = "PO-LOCAL-00001",
            SupplierId = supplier.Id,
            PurchaseDate = DateTimeOffset.UtcNow,
            Status = PurchaseStatus.Draft,
            PaymentStatus = PurchasePaymentStatus.Unpaid,
        };

        using (var context = _factory.CreateDbContext())
        {
            context.Suppliers.Add(supplier);
            context.Purchases.Add(purchase);
            await context.SaveChangesAsync();
        }

        await _sut.ApplyServerPurchaseNumberAsync(purchase.Id, "PO-2026-000123");

        using var verifyContext = _factory.CreateDbContext();
        var reloaded = await verifyContext.Purchases.SingleAsync(p => p.Id == purchase.Id);
        Assert.Equal("PO-2026-000123", reloaded.PurchaseNumber);
    }

    public void Dispose()
    {
        if (File.Exists(_databaseFilePath))
        {
            File.Delete(_databaseFilePath);
        }
    }

    /// <summary>
    /// Points at a temp file instead of AppPaths.DatabaseFilePath (which
    /// the real SyncDbContextFactory hardcodes) - otherwise identical:
    /// a fresh InventoryDbContext per call, same interceptors.
    /// </summary>
    private sealed class FakeSyncDbContextFactory : ISyncDbContextFactory
    {
        private readonly string _databaseFilePath;
        private readonly AuditableEntitySaveChangesInterceptor _auditInterceptor = new(new SystemDateTimeProvider());
        private readonly SqliteConnectionInterceptor _connectionInterceptor = new();

        public FakeSyncDbContextFactory(string databaseFilePath)
        {
            _databaseFilePath = databaseFilePath;
        }

        public InventoryDbContext CreateDbContext()
        {
            var optionsBuilder = new DbContextOptionsBuilder<InventoryDbContext>()
                .UseSqlite($"Data Source={_databaseFilePath}")
                .AddInterceptors(_auditInterceptor, _connectionInterceptor);

            return new InventoryDbContext(optionsBuilder.Options);
        }
    }
}
