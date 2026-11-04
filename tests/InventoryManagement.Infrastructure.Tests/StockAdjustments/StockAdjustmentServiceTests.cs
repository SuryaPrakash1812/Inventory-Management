using InventoryManagement.Application.Inventory;
using InventoryManagement.Application.StockAdjustments;
using InventoryManagement.Domain.Entities;
using InventoryManagement.Domain.Enums;
using InventoryManagement.Infrastructure.Auth;
using InventoryManagement.Infrastructure.Common;
using InventoryManagement.Infrastructure.Data;
using InventoryManagement.Infrastructure.Data.Interceptors;
using InventoryManagement.Infrastructure.Inventory;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InventoryManagement.Infrastructure.Tests.StockAdjustments;

public sealed class StockAdjustmentServiceTests : IDisposable
{
    private readonly string _databaseFilePath;
    private readonly InventoryDbContext _context;
    private readonly InventoryService _inventoryService;
    private readonly Infrastructure.StockAdjustments.StockAdjustmentService _sut;
    private readonly Category _category;
    private readonly Product _product;

    public StockAdjustmentServiceTests()
    {
        _databaseFilePath = Path.Combine(Path.GetTempPath(), $"InventoryManagementStockAdjustmentTests_{Guid.NewGuid()}.db");

        var optionsBuilder = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite($"Data Source={_databaseFilePath}")
            .AddInterceptors(
                new AuditableEntitySaveChangesInterceptor(new SystemDateTimeProvider()),
                new SqliteConnectionInterceptor());

        _context = new InventoryDbContext(optionsBuilder.Options);
        _context.Database.EnsureCreated();

        _category = new Category { Name = "Beverages" };
        _product = new Product
        {
            Sku = "SKU-1", Name = "Cola", Category = _category, CostPrice = 5m, SellingPrice = 10m,
            QuantityOnHand = 50m,
        };
        _context.Categories.Add(_category);
        _context.Products.Add(_product);
        _context.SaveChanges();

        var session = new CurrentUserSession();
        _inventoryService = new InventoryService(_context, new SystemDateTimeProvider(), session);
        _sut = new Infrastructure.StockAdjustments.StockAdjustmentService(
            _context, _inventoryService, new AuditLogger(_context, new SystemDateTimeProvider(), session), new SystemDateTimeProvider());
    }

    private SaveDraftStockAdjustmentRequest MakeDraftRequest(
        Guid? adjustmentId = null, decimal quantityChange = 5, StockAdjustmentReason reason = StockAdjustmentReason.Correction) =>
        new(
            adjustmentId, DateTimeOffset.UtcNow, reason, "Test adjustment",
            new[] { new StockAdjustmentItemRequest(_product.Id, quantityChange, "Line note") });

    [Fact]
    public async Task SaveDraftAsync_CreatesNewDraft_WithNoStockChangeYet()
    {
        var result = await _sut.SaveDraftAsync(MakeDraftRequest(quantityChange: 10));

        Assert.True(result.IsSuccess);
        Assert.Equal(StockAdjustmentStatus.Draft, result.Value.Status);
        Assert.StartsWith("ADJ-", result.Value.AdjustmentNumber);

        var stock = await _inventoryService.GetCurrentStockAsync(_product.Id);
        Assert.Equal(50m, stock); // unchanged - Draft never touches stock
    }

    private SaveDraftStockAdjustmentRequest MakeLinesRequest(params (Guid ProductId, decimal Change)[] lines) =>
        MakeDraftRequest() with { Items = lines.Select(l => new StockAdjustmentItemRequest(l.ProductId, l.Change, null)).ToList() };

    [Fact]
    public async Task ConfirmAsync_DecreasesBeyondStockInTotal_Fails_AndChangesNothing()
    {
        var created = await _sut.SaveDraftAsync(MakeLinesRequest((_product.Id, -30m), (_product.Id, -30m)));

        var result = await _sut.ConfirmAsync(created.Value.Id);

        Assert.True(result.IsFailure);
        Assert.Contains("Cola", result.Error);
        Assert.Equal(50m, await _inventoryService.GetCurrentStockAsync(_product.Id));
        Assert.Empty(await _context.StockMovements.ToListAsync());
    }

    /// <summary>The order lines were entered must not matter: -60 then +20 is a valid net change of -40 from a stock of 50.</summary>
    [Fact]
    public async Task ConfirmAsync_OffsettingLinesOfOneProduct_AreJudgedByTheirNet()
    {
        var created = await _sut.SaveDraftAsync(MakeLinesRequest((_product.Id, -60m), (_product.Id, 20m)));

        var confirmed = await _sut.ConfirmAsync(created.Value.Id);

        Assert.True(confirmed.IsSuccess, confirmed.IsFailure ? confirmed.Error : null);
        Assert.Equal(10m, await _inventoryService.GetCurrentStockAsync(_product.Id));

        var cancelled = await _sut.CancelAsync(created.Value.Id);

        Assert.True(cancelled.IsSuccess, cancelled.IsFailure ? cancelled.Error : null);
        Assert.Equal(50m, await _inventoryService.GetCurrentStockAsync(_product.Id));
    }

    /// <summary>Regression: +30 and +30 were each checked alone against a stock of 40, and the first was reversed before the second failed.</summary>
    [Fact]
    public async Task CancelAsync_ChecksNetIncreaseAcrossLines_AndChangesNothingWhenShort()
    {
        var created = await _sut.SaveDraftAsync(MakeLinesRequest((_product.Id, 30m), (_product.Id, 30m)));
        await _sut.ConfirmAsync(created.Value.Id);
        await _inventoryService.RemoveStockAsync(new RemoveStockRequest(
            _product.Id, 70m, StockMovementType.SaleIssue, StockReferenceType.Sale, Guid.NewGuid()));

        var result = await _sut.CancelAsync(created.Value.Id);

        Assert.True(result.IsFailure);
        Assert.Contains("only 40 remaining, needed 60", result.Error);
        Assert.Equal(40m, await _inventoryService.GetCurrentStockAsync(_product.Id));
        Assert.Equal(StockAdjustmentStatus.Confirmed, (await _sut.GetStockAdjustmentByIdAsync(created.Value.Id))!.Status);
    }

    /// <summary>The larger increase applies first, so its failure-free movement is what must be undone when the second product's movement fails.</summary>
    [Fact]
    public async Task ConfirmAsync_WhenALaterLineFails_RollsBackEveryLine()
    {
        var other = new Product { Sku = "SKU-2", Name = "Lemonade", Category = _category };
        _context.Products.Add(other);
        await _context.SaveChangesAsync();

        var created = await _sut.SaveDraftAsync(MakeLinesRequest((_product.Id, 10m), (other.Id, 4m)));

        var tracked = await _context.Products.SingleAsync(p => p.Id == other.Id);
        tracked.MarkDeleted(DateTimeOffset.UtcNow, null);
        await _context.SaveChangesAsync();

        var result = await _sut.ConfirmAsync(created.Value.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(50m, await _inventoryService.GetCurrentStockAsync(_product.Id));
        Assert.Empty(await _context.StockMovements.ToListAsync());
        Assert.Equal(StockAdjustmentStatus.Draft, (await _context.StockAdjustments.AsNoTracking().SingleAsync(a => a.Id == created.Value.Id)).Status);
    }

    [Fact]
    public async Task CancelAsync_WithReason_AddsItToTheNotes()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest());

        var cancelled = await _sut.CancelAsync(created.Value.Id, "  Counted wrong  ");

        Assert.True(cancelled.IsSuccess);
        Assert.Contains(": Counted wrong", cancelled.Value.Notes);
    }

    /// <summary>The dialog blocks exactly when the service would: this preview is what it shows for cancelling +30, +30 with 40 left.</summary>
    [Fact]
    public async Task PreviewOfReversal_AgreesWithCancelAsync()
    {
        var created = await _sut.SaveDraftAsync(MakeLinesRequest((_product.Id, 30m), (_product.Id, 30m)));
        var confirmed = await _sut.ConfirmAsync(created.Value.Id);
        await _inventoryService.RemoveStockAsync(new RemoveStockRequest(
            _product.Id, 70m, StockMovementType.SaleIssue, StockReferenceType.Sale, Guid.NewGuid()));

        var lines = await _inventoryService.PreviewStockChangesAsync(
            confirmed.Value.Items.Select(i => new StockChange(i.ProductId, -i.QuantityChange)).ToList());

        Assert.False(Assert.Single(lines).IsPossible);
        Assert.True((await _sut.CancelAsync(created.Value.Id)).IsFailure);
    }

    [Fact]
    public async Task SaveDraftAsync_WithZeroQuantityChange_Fails()
    {
        var result = await _sut.SaveDraftAsync(MakeDraftRequest(quantityChange: 0));

        Assert.True(result.IsFailure);
        Assert.Equal("Adjustment quantity must be a nonzero increase or decrease.", result.Error);
    }

    [Fact]
    public async Task SaveDraftAsync_WithNoItems_Fails()
    {
        var request = new SaveDraftStockAdjustmentRequest(
            null, DateTimeOffset.UtcNow, StockAdjustmentReason.Correction, null, Array.Empty<StockAdjustmentItemRequest>());

        var result = await _sut.SaveDraftAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal("At least one item is required.", result.Error);
    }

    [Fact]
    public async Task SaveDraftAsync_WithNonexistentProduct_Fails()
    {
        var request = new SaveDraftStockAdjustmentRequest(
            null, DateTimeOffset.UtcNow, StockAdjustmentReason.Correction, null,
            new[] { new StockAdjustmentItemRequest(Guid.NewGuid(), 5, null) });

        var result = await _sut.SaveDraftAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal("One of the items refers to a product that no longer exists.", result.Error);
    }

    [Fact]
    public async Task SaveDraftAsync_ValidationFailure_CreatesNothing()
    {
        var countBefore = await _context.StockAdjustments.CountAsync();

        await _sut.SaveDraftAsync(MakeDraftRequest(quantityChange: 0));

        Assert.Equal(countBefore, await _context.StockAdjustments.CountAsync());
    }

    [Fact]
    public async Task ConfirmAsync_WithIncrease_RaisesStockAndCreatesAdjustmentIncreaseMovement()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest(quantityChange: 15));

        var confirmed = await _sut.ConfirmAsync(created.Value.Id);

        Assert.True(confirmed.IsSuccess);
        Assert.Equal(StockAdjustmentStatus.Confirmed, confirmed.Value.Status);

        var stock = await _inventoryService.GetCurrentStockAsync(_product.Id);
        Assert.Equal(65m, stock); // 50 + 15

        var movement = await _context.StockMovements.SingleAsync(m => m.ReferenceId == created.Value.Id);
        Assert.Equal(StockMovementType.AdjustmentIncrease, movement.MovementType);
        Assert.Equal(15m, movement.QuantityChange);

        var item = confirmed.Value.Items.Single();
        Assert.Equal(50m, item.QuantityBefore);
        Assert.Equal(65m, item.QuantityAfter);
    }

    [Fact]
    public async Task ConfirmAsync_WithDecrease_LowersStockAndCreatesAdjustmentDecreaseMovement()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest(quantityChange: -20));

        var confirmed = await _sut.ConfirmAsync(created.Value.Id);

        Assert.True(confirmed.IsSuccess);

        var stock = await _inventoryService.GetCurrentStockAsync(_product.Id);
        Assert.Equal(30m, stock); // 50 - 20

        var movement = await _context.StockMovements.SingleAsync(m => m.ReferenceId == created.Value.Id);
        Assert.Equal(StockMovementType.AdjustmentDecrease, movement.MovementType);
    }

    [Fact]
    public async Task ConfirmAsync_DecreaseBelowZero_FailsAndChangesNothing()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest(quantityChange: -100)); // only 50 in stock

        var confirmed = await _sut.ConfirmAsync(created.Value.Id);

        Assert.True(confirmed.IsFailure);

        var stock = await _inventoryService.GetCurrentStockAsync(_product.Id);
        Assert.Equal(50m, stock); // unchanged

        Assert.Empty(await _context.StockMovements.Where(m => m.ReferenceId == created.Value.Id).ToListAsync());
    }

    [Fact]
    public async Task ConfirmAsync_CalledTwice_IsIdempotent_NeverDoublesStock()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest(quantityChange: 10));
        await _sut.ConfirmAsync(created.Value.Id);

        var secondConfirm = await _sut.ConfirmAsync(created.Value.Id);

        Assert.True(secondConfirm.IsFailure);
        Assert.Equal("Stock adjustment is already Confirmed and cannot be confirmed again.", secondConfirm.Error);

        var stock = await _inventoryService.GetCurrentStockAsync(_product.Id);
        Assert.Equal(60m, stock); // only applied once
    }

    [Fact]
    public async Task CancelAsync_FromDraft_Succeeds_WithNoStockMovements()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest(quantityChange: 10));

        var cancelled = await _sut.CancelAsync(created.Value.Id);

        Assert.True(cancelled.IsSuccess);
        Assert.Equal(StockAdjustmentStatus.Cancelled, cancelled.Value.Status);
        Assert.Empty(await _context.StockMovements.Where(m => m.ReferenceId == created.Value.Id).ToListAsync());
    }

    [Fact]
    public async Task CancelAsync_FromConfirmedIncrease_ReversesStock()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest(quantityChange: 10));
        await _sut.ConfirmAsync(created.Value.Id);

        var cancelled = await _sut.CancelAsync(created.Value.Id);

        Assert.True(cancelled.IsSuccess);
        var stock = await _inventoryService.GetCurrentStockAsync(_product.Id);
        Assert.Equal(50m, stock); // back to original
    }

    [Fact]
    public async Task CancelAsync_WhenIncreaseAlreadyPartlyConsumed_FailsWithNoEffect()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest(quantityChange: 10));
        await _sut.ConfirmAsync(created.Value.Id); // stock now 60

        // Consume most of the added stock via a separate decrease adjustment.
        var consume = await _sut.SaveDraftAsync(MakeDraftRequest(quantityChange: -55));
        await _sut.ConfirmAsync(consume.Value.Id); // stock now 5

        var cancelled = await _sut.CancelAsync(created.Value.Id); // needs to remove 10, only 5 left

        Assert.True(cancelled.IsFailure);
        Assert.Contains("already been used elsewhere", cancelled.Error);

        var stock = await _inventoryService.GetCurrentStockAsync(_product.Id);
        Assert.Equal(5m, stock); // unchanged by the failed cancellation
    }

    [Fact]
    public async Task CancelAsync_CalledTwice_IsIdempotent()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest(quantityChange: 10));
        await _sut.ConfirmAsync(created.Value.Id);
        await _sut.CancelAsync(created.Value.Id);

        var secondCancel = await _sut.CancelAsync(created.Value.Id);

        Assert.True(secondCancel.IsFailure);
        Assert.Equal("Stock adjustment is already cancelled.", secondCancel.Error);
    }

    [Fact]
    public async Task DeleteDraftAsync_OnConfirmedAdjustment_Fails()
    {
        var created = await _sut.SaveDraftAsync(MakeDraftRequest());
        await _sut.ConfirmAsync(created.Value.Id);

        var result = await _sut.DeleteDraftAsync(created.Value.Id);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task AdjustmentNumbers_AreUniqueAcrossMultipleDrafts()
    {
        var first = await _sut.SaveDraftAsync(MakeDraftRequest());
        var second = await _sut.SaveDraftAsync(MakeDraftRequest());

        Assert.NotEqual(first.Value.AdjustmentNumber, second.Value.AdjustmentNumber);
    }

    public void Dispose()
    {
        _context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_databaseFilePath))
        {
            File.Delete(_databaseFilePath);
        }
    }
}
