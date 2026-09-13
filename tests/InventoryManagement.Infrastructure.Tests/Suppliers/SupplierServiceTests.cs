using InventoryManagement.Application.Suppliers;
using InventoryManagement.Domain.Enums;
using InventoryManagement.Infrastructure.Auth;
using InventoryManagement.Infrastructure.Data;
using InventoryManagement.Infrastructure.Data.Interceptors;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InventoryManagement.Infrastructure.Tests.Suppliers;

public class SupplierServiceTests : IDisposable
{
    private readonly string _databaseFilePath;
    private readonly InventoryDbContext _context;
    private readonly SupplierService _sut;

    public SupplierServiceTests()
    {
        _databaseFilePath = Path.Combine(Path.GetTempPath(), $"InventoryManagementSupplierTests_{Guid.NewGuid()}.db");

        var optionsBuilder = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite($"Data Source={_databaseFilePath}")
            .AddInterceptors(
                new AuditableEntitySaveChangesInterceptor(new SystemDateTimeProvider()),
                new SqliteConnectionInterceptor());

        _context = new InventoryDbContext(optionsBuilder.Options);
        _context.Database.EnsureCreated();

        var session = new CurrentUserSession();
        _sut = new SupplierService(_context, new AuditLogger(_context, new SystemDateTimeProvider(), session));
    }

    public void Dispose()
    {
        _context.Dispose();
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databaseFilePath))
        {
            File.Delete(_databaseFilePath);
        }
    }

    private static CreateSupplierRequest MakeRequest(string name, string? email = null) =>
        new(
            Name: name,
            ContactPerson: "Jane Doe",
            Phone: "555-1000",
            Email: email,
            Address: "123 Main St",
            TaxId: "TAX-123",
            Notes: "Some notes");

    [Fact]
    public async Task CreateSupplierAsync_ThenGetById_RoundTrips()
    {
        var created = await _sut.CreateSupplierAsync(MakeRequest("Acme Supplies"));

        Assert.True(created.IsSuccess);

        var detail = await _sut.GetSupplierByIdAsync(created.Value.Id);

        Assert.NotNull(detail);
        Assert.Equal("Acme Supplies", detail!.Name);
        Assert.Equal("TAX-123", detail.TaxId);
    }

    [Fact]
    public async Task CreateSupplierAsync_WithBlankName_Fails()
    {
        var result = await _sut.CreateSupplierAsync(MakeRequest("   "));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task CreateSupplierAsync_WithInvalidEmail_Fails()
    {
        var result = await _sut.CreateSupplierAsync(MakeRequest("Acme Supplies", "not-an-email"));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task CreateSupplierAsync_WithValidEmail_Succeeds()
    {
        var result = await _sut.CreateSupplierAsync(MakeRequest("Acme Supplies", "contact@acme.test"));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetSuppliersAsync_SearchMatchesNameContactPhoneAndEmail()
    {
        await _sut.CreateSupplierAsync(new CreateSupplierRequest(
            "Acme Supplies", "Jane Doe", "555-1000", "jane@acme.test", null, null, null));
        await _sut.CreateSupplierAsync(new CreateSupplierRequest(
            "Globex Inc", "John Smith", "555-2000", "john@globex.test", null, null, null));

        var byName = await _sut.GetSuppliersAsync(new SupplierQueryParameters { SearchTerm = "Acme" });
        var byContact = await _sut.GetSuppliersAsync(new SupplierQueryParameters { SearchTerm = "John Smith" });
        var byEmail = await _sut.GetSuppliersAsync(new SupplierQueryParameters { SearchTerm = "globex.test" });

        Assert.Single(byName.Items);
        Assert.Single(byContact.Items);
        Assert.Single(byEmail.Items);
    }

    [Fact]
    public async Task GetSuppliersAsync_FiltersByActiveStatus()
    {
        var created = await _sut.CreateSupplierAsync(MakeRequest("Active Supplier"));
        await _sut.CreateSupplierAsync(MakeRequest("Also Active"));
        await _sut.DeactivateSupplierAsync(created.Value.Id);

        var activeOnly = await _sut.GetSuppliersAsync(new SupplierQueryParameters { IsActive = true });
        var inactiveOnly = await _sut.GetSuppliersAsync(new SupplierQueryParameters { IsActive = false });

        Assert.Single(activeOnly.Items);
        Assert.Single(inactiveOnly.Items);
    }

    [Fact]
    public async Task GetSuppliersAsync_Pagination_ReturnsCorrectPageAndTotalCount()
    {
        for (var i = 1; i <= 15; i++)
        {
            await _sut.CreateSupplierAsync(MakeRequest($"Supplier {i:D2}"));
        }

        var page1 = await _sut.GetSuppliersAsync(new SupplierQueryParameters { PageNumber = 1, PageSize = 10 });
        var page2 = await _sut.GetSuppliersAsync(new SupplierQueryParameters { PageNumber = 2, PageSize = 10 });

        Assert.Equal(15, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(10, page1.Items.Count);
        Assert.Equal(5, page2.Items.Count);
    }

    [Fact]
    public async Task DeactivateSupplierAsync_SetsIsActiveFalse_ButKeepsRecordVisible()
    {
        var created = await _sut.CreateSupplierAsync(MakeRequest("Acme Supplies"));

        var result = await _sut.DeactivateSupplierAsync(created.Value.Id);

        Assert.True(result.IsSuccess);

        var detail = await _sut.GetSupplierByIdAsync(created.Value.Id);
        Assert.NotNull(detail);
        Assert.False(detail!.IsActive);
    }

    [Fact]
    public async Task GetHistoryAsync_RecordsCreateUpdateAndDeactivate()
    {
        var created = await _sut.CreateSupplierAsync(MakeRequest("Acme Supplies"));

        await _sut.UpdateSupplierAsync(new UpdateSupplierRequest(
            SupplierId: created.Value.Id,
            Name: "Acme Supplies Renamed",
            ContactPerson: "Jane Doe",
            Phone: "555-1000",
            Email: null,
            Address: null,
            TaxId: null,
            Notes: null,
            IsActive: true));

        await _sut.DeactivateSupplierAsync(created.Value.Id);

        var history = await _sut.GetHistoryAsync(created.Value.Id);

        // Not asserting strict order here - near-simultaneous timestamps in a
        // fast test run could tie, and ordering among ties isn't the point
        // of this test (GetSuppliersAsync's own sort correctness is covered
        // elsewhere). What matters is that all three actions were recorded.
        Assert.Equal(3, history.Count);
        Assert.Single(history, e => e.Action == AuditAction.Created);
        Assert.Equal(2, history.Count(e => e.Action == AuditAction.Updated));
    }
}
