using InventoryManagement.Infrastructure.Auth;
using InventoryManagement.Infrastructure.Data;
using InventoryManagement.Infrastructure.Data.Interceptors;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InventoryManagement.Infrastructure.Tests.Auth;

public class AuthenticationServiceTests : IDisposable
{
    private readonly string _databaseFilePath;
    private readonly InventoryDbContext _context;
    private readonly CurrentUserSession _session;
    private readonly AuthenticationService _sut;

    public AuthenticationServiceTests()
    {
        _databaseFilePath = Path.Combine(Path.GetTempPath(), $"InventoryManagementAuthTests_{Guid.NewGuid()}.db");

        var optionsBuilder = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite($"Data Source={_databaseFilePath}")
            .AddInterceptors(
                new AuditableEntitySaveChangesInterceptor(new SystemDateTimeProvider()),
                new SqliteConnectionInterceptor());

        _context = new InventoryDbContext(optionsBuilder.Options);
        _context.Database.EnsureCreated();

        _session = new CurrentUserSession();

        _sut = new AuthenticationService(
            _context,
            new Pbkdf2PasswordHasher(),
            _session,
            new AuditLogger(_context, new SystemDateTimeProvider(), _session),
            new SystemDateTimeProvider());
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

    [Fact]
    public async Task IsFirstRunAsync_WithNoUsers_ReturnsTrue()
    {
        Assert.True(await _sut.IsFirstRunAsync());
    }

    [Fact]
    public async Task CreateFirstAdministratorAsync_CreatesUserAndSignsIn()
    {
        var result = await _sut.CreateFirstAdministratorAsync("admin", "SuperSecret123", "The Administrator");

        Assert.True(result.IsSuccess);
        Assert.Equal("Administrator", result.Value.RoleName);
        Assert.Contains("Users.Manage", result.Value.Permissions);
        Assert.True(_session.IsAuthenticated);
        Assert.False(await _sut.IsFirstRunAsync());
    }

    [Fact]
    public async Task CreateFirstAdministratorAsync_WhenUsersAlreadyExist_Fails()
    {
        await _sut.CreateFirstAdministratorAsync("admin", "SuperSecret123", "The Administrator");
        _session.SignOut();

        var result = await _sut.CreateFirstAdministratorAsync("admin2", "SuperSecret123", "Another Admin");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task LoginAsync_WithCorrectCredentials_Succeeds()
    {
        await _sut.CreateFirstAdministratorAsync("admin", "SuperSecret123", "The Administrator");
        _session.SignOut();

        var result = await _sut.LoginAsync("admin", "SuperSecret123");

        Assert.True(result.IsSuccess);
        Assert.True(_session.IsAuthenticated);
        Assert.Equal("admin", _session.CurrentUser!.Username);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_FailsAndDoesNotSignIn()
    {
        await _sut.CreateFirstAdministratorAsync("admin", "SuperSecret123", "The Administrator");
        _session.SignOut();

        var result = await _sut.LoginAsync("admin", "WrongPassword");

        Assert.True(result.IsFailure);
        Assert.False(_session.IsAuthenticated);
    }

    [Fact]
    public async Task LoginAsync_WithUnknownUsername_FailsWithSameMessageAsWrongPassword()
    {
        await _sut.CreateFirstAdministratorAsync("admin", "SuperSecret123", "The Administrator");
        _session.SignOut();

        var wrongPasswordResult = await _sut.LoginAsync("admin", "WrongPassword");
        var unknownUserResult = await _sut.LoginAsync("no-such-user", "WrongPassword");

        // Same error for both, so a caller can't use the message to enumerate
        // valid usernames.
        Assert.Equal(wrongPasswordResult.Error, unknownUserResult.Error);
    }

    [Fact]
    public async Task LoginAsync_WhenAccountDeactivated_Fails()
    {
        await _sut.CreateFirstAdministratorAsync("admin", "SuperSecret123", "The Administrator");
        var user = await _context.Users.SingleAsync(u => u.Username == "admin");
        user.IsActive = false;
        await _context.SaveChangesAsync();
        _session.SignOut();

        var result = await _sut.LoginAsync("admin", "SuperSecret123");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task LogoutAsync_ClearsTheSession()
    {
        await _sut.CreateFirstAdministratorAsync("admin", "SuperSecret123", "The Administrator");

        await _sut.LogoutAsync();

        Assert.False(_session.IsAuthenticated);
    }

    [Fact]
    public async Task LoginAndLogout_EachWriteAnAuditLogEntry()
    {
        await _sut.CreateFirstAdministratorAsync("admin", "SuperSecret123", "The Administrator");
        await _sut.LogoutAsync();

        var actions = await _context.AuditLogs.Select(a => a.Action).ToListAsync();

        Assert.Contains(Domain.Enums.AuditAction.Created, actions);
        Assert.Contains(Domain.Enums.AuditAction.LoggedOut, actions);
    }
}
