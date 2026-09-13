using InventoryManagement.Application.Customers;
using InventoryManagement.Domain.Enums;
using InventoryManagement.Infrastructure.Auth;
using InventoryManagement.Infrastructure.Data;
using InventoryManagement.Infrastructure.Data.Interceptors;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InventoryManagement.Infrastructure.Tests.Customers;

public class CustomerServiceTests : IDisposable
{
    private readonly string _databaseFilePath;
    private readonly InventoryDbContext _context;
    private readonly CustomerService _sut;

    public CustomerServiceTests()
    {
        _databaseFilePath = Path.Combine(Path.GetTempPath(), $"InventoryManagementCustomerTests_{Guid.NewGuid()}.db");

        var optionsBuilder = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite($"Data Source={_databaseFilePath}")
            .AddInterceptors(
                new AuditableEntitySaveChangesInterceptor(new SystemDateTimeProvider()),
                new SqliteConnectionInterceptor());

        _context = new InventoryDbContext(optionsBuilder.Options);
        _context.Database.EnsureCreated();

        var session = new CurrentUserSession();
        _sut = new CustomerService(_context, new AuditLogger(_context, new SystemDateTimeProvider(), session));
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

    private static CreateCustomerRequest MakeRequest(string name, string? email = null) =>
        new(
            Name: name,
            ContactPerson: null,
            Phone: "555-1000",
            Email: email,
            Address: "123 Main St",
            TaxId: "TAX-123",
            Notes: "Some notes");

    [Fact]
    public async Task CreateCustomerAsync_ThenGetById_RoundTrips()
    {
        var created = await _sut.CreateCustomerAsync(MakeRequest("Retail Customer"));

        Assert.True(created.IsSuccess);

        var detail = await _sut.GetCustomerByIdAsync(created.Value.Id);

        Assert.NotNull(detail);
        Assert.Equal("Retail Customer", detail!.Name);
        Assert.Equal("TAX-123", detail.TaxId);
    }

    [Fact]
    public async Task CreateCustomerAsync_WithBlankName_Fails()
    {
        var result = await _sut.CreateCustomerAsync(MakeRequest("   "));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task CreateCustomerAsync_WithInvalidEmail_Fails()
    {
        var result = await _sut.CreateCustomerAsync(MakeRequest("Retail Customer", "not-an-email"));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task GetCustomersAsync_SearchMatchesNamePhoneAndEmail()
    {
        await _sut.CreateCustomerAsync(new CreateCustomerRequest(
            "Alice Anderson", null, "555-1000", "alice@example.test", null, null, null));
        await _sut.CreateCustomerAsync(new CreateCustomerRequest(
            "Bob Baker", null, "555-2000", "bob@example.test", null, null, null));

        var byName = await _sut.GetCustomersAsync(new CustomerQueryParameters { SearchTerm = "Alice" });
        var byPhone = await _sut.GetCustomersAsync(new CustomerQueryParameters { SearchTerm = "555-2000" });

        Assert.Single(byName.Items);
        Assert.Single(byPhone.Items);
    }

    [Fact]
    public async Task GetCustomersAsync_FiltersByActiveStatus()
    {
        var created = await _sut.CreateCustomerAsync(MakeRequest("Active Customer"));
        await _sut.CreateCustomerAsync(MakeRequest("Also Active"));
        await _sut.DeactivateCustomerAsync(created.Value.Id);

        var activeOnly = await _sut.GetCustomersAsync(new CustomerQueryParameters { IsActive = true });
        var inactiveOnly = await _sut.GetCustomersAsync(new CustomerQueryParameters { IsActive = false });

        Assert.Single(activeOnly.Items);
        Assert.Single(inactiveOnly.Items);
    }

    [Fact]
    public async Task GetCustomersAsync_Pagination_ReturnsCorrectPageAndTotalCount()
    {
        for (var i = 1; i <= 15; i++)
        {
            await _sut.CreateCustomerAsync(MakeRequest($"Customer {i:D2}"));
        }

        var page1 = await _sut.GetCustomersAsync(new CustomerQueryParameters { PageNumber = 1, PageSize = 10 });
        var page2 = await _sut.GetCustomersAsync(new CustomerQueryParameters { PageNumber = 2, PageSize = 10 });

        Assert.Equal(15, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(10, page1.Items.Count);
        Assert.Equal(5, page2.Items.Count);
    }

    [Fact]
    public async Task DeactivateCustomerAsync_SetsIsActiveFalse_ButKeepsRecordVisible()
    {
        var created = await _sut.CreateCustomerAsync(MakeRequest("Retail Customer"));

        var result = await _sut.DeactivateCustomerAsync(created.Value.Id);

        Assert.True(result.IsSuccess);

        var detail = await _sut.GetCustomerByIdAsync(created.Value.Id);
        Assert.NotNull(detail);
        Assert.False(detail!.IsActive);
    }

    [Fact]
    public async Task GetHistoryAsync_RecordsCreateUpdateAndDeactivate()
    {
        var created = await _sut.CreateCustomerAsync(MakeRequest("Retail Customer"));

        await _sut.UpdateCustomerAsync(new UpdateCustomerRequest(
            CustomerId: created.Value.Id,
            Name: "Retail Customer Renamed",
            ContactPerson: null,
            Phone: "555-1000",
            Email: null,
            Address: null,
            TaxId: null,
            Notes: null,
            IsActive: true));

        await _sut.DeactivateCustomerAsync(created.Value.Id);

        var history = await _sut.GetHistoryAsync(created.Value.Id);

        Assert.Equal(3, history.Count);
        Assert.Single(history, e => e.Action == AuditAction.Created);
        Assert.Equal(2, history.Count(e => e.Action == AuditAction.Updated));
    }
}
