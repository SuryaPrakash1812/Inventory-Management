using InventoryManagement.Application.Users;
using InventoryManagement.Infrastructure.Auth;
using InventoryManagement.Infrastructure.Common;
using InventoryManagement.Infrastructure.Data;
using InventoryManagement.Infrastructure.Data.Interceptors;
using InventoryManagement.Infrastructure.Users;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InventoryManagement.Infrastructure.Tests.Users;

/// <summary>
/// Closes the gap the audit stage explicitly flagged: Users/Roles had
/// full working functionality (a 183-line WPF page, two Application
/// services) and zero automated tests before this.
/// </summary>
public sealed class UserRoleManagementServiceTests : IDisposable
{
    private readonly string _databaseFilePath;
    private readonly InventoryDbContext _context;
    private readonly UserManagementService _userService;
    private readonly RoleManagementService _roleService;

    public UserRoleManagementServiceTests()
    {
        _databaseFilePath = Path.Combine(Path.GetTempPath(), $"InventoryManagementUserTests_{Guid.NewGuid()}.db");

        var optionsBuilder = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite($"Data Source={_databaseFilePath}")
            .AddInterceptors(
                new AuditableEntitySaveChangesInterceptor(new SystemDateTimeProvider()),
                new SqliteConnectionInterceptor());

        _context = new InventoryDbContext(optionsBuilder.Options);
        _context.Database.EnsureCreated();

        var session = new CurrentUserSession();
        var auditLogger = new AuditLogger(_context, new SystemDateTimeProvider(), session);
        _userService = new UserManagementService(_context, new Pbkdf2PasswordHasher(), auditLogger, session);
        _roleService = new RoleManagementService(_context, auditLogger, session, new SystemDateTimeProvider());
    }

    [Fact]
    public async Task CreateUserAsync_WithValidData_Succeeds()
    {
        var result = await _userService.CreateUserAsync(
            new CreateUserRequest("jdoe", "Str0ngP@ssword!", "Jane Doe", "jane@example.com", RoleId: null));

        Assert.True(result.IsSuccess);
        Assert.Equal("jdoe", result.Value.Username);
    }

    [Fact]
    public async Task CreateUserAsync_DoesNotStorePlaintextPassword()
    {
        var result = await _userService.CreateUserAsync(
            new CreateUserRequest("jdoe", "Str0ngP@ssword!", "Jane Doe", null, RoleId: null));

        var stored = await _context.Users.SingleAsync(u => u.Id == result.Value.Id);

        Assert.DoesNotContain("Str0ngP@ssword!", stored.PasswordHash);
        Assert.NotEqual("Str0ngP@ssword!", stored.PasswordHash);
    }

    [Fact]
    public async Task CreateUserAsync_WithDuplicateUsername_Fails()
    {
        await _userService.CreateUserAsync(new CreateUserRequest("jdoe", "Password1!", "Jane Doe", null, null));

        var duplicate = await _userService.CreateUserAsync(new CreateUserRequest("jdoe", "Password2!", "John Doe", null, null));

        Assert.True(duplicate.IsFailure);
    }

    [Theory]
    [InlineData("", "Password1!", "Jane Doe")]
    [InlineData("jdoe", "", "Jane Doe")]
    [InlineData("jdoe", "Password1!", "")]
    public async Task CreateUserAsync_WithMissingRequiredFields_Fails(string username, string password, string fullName)
    {
        var result = await _userService.CreateUserAsync(new CreateUserRequest(username, password, fullName, null, null));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task UpdateUserAsync_ChangesFullNameAndEmail()
    {
        var created = await _userService.CreateUserAsync(new CreateUserRequest("jdoe", "Password1!", "Jane Doe", null, null));

        var updated = await _userService.UpdateUserAsync(
            new UpdateUserRequest(created.Value.Id, "Jane Smith", "jane.smith@example.com", RoleId: null, IsActive: true));

        Assert.True(updated.IsSuccess);
        Assert.Equal("Jane Smith", updated.Value.FullName);
    }

    [Fact]
    public async Task DeactivateUserAsync_SetsIsActiveFalse_ButUserStillVisible()
    {
        var created = await _userService.CreateUserAsync(new CreateUserRequest("jdoe", "Password1!", "Jane Doe", null, null));

        var result = await _userService.DeactivateUserAsync(created.Value.Id);

        Assert.True(result.IsSuccess);

        var users = await _userService.GetUsersAsync();
        var deactivated = users.Single(u => u.Id == created.Value.Id);
        Assert.False(deactivated.IsActive);
    }

    [Fact]
    public async Task DeactivateUserAsync_IsReversible_ViaUpdateUserAsync()
    {
        var created = await _userService.CreateUserAsync(new CreateUserRequest("jdoe", "Password1!", "Jane Doe", null, null));
        await _userService.DeactivateUserAsync(created.Value.Id);

        var reactivated = await _userService.UpdateUserAsync(
            new UpdateUserRequest(created.Value.Id, "Jane Doe", null, RoleId: null, IsActive: true));

        Assert.True(reactivated.IsSuccess);
        var users = await _userService.GetUsersAsync();
        Assert.True(users.Single(u => u.Id == created.Value.Id).IsActive);
    }

    [Fact]
    public async Task ChangePasswordAsync_UpdatesTheStoredHash()
    {
        var created = await _userService.CreateUserAsync(new CreateUserRequest("jdoe", "OldPassword1!", "Jane Doe", null, null));
        var originalHash = (await _context.Users.SingleAsync(u => u.Id == created.Value.Id)).PasswordHash;

        var result = await _userService.ChangePasswordAsync(new ChangePasswordRequest(created.Value.Id, "NewPassword1!"));

        Assert.True(result.IsSuccess);
        var newHash = (await _context.Users.SingleAsync(u => u.Id == created.Value.Id)).PasswordHash;
        Assert.NotEqual(originalHash, newHash);
    }

    [Fact]
    public async Task SaveRoleAsync_CreatesNewRole()
    {
        var permissions = await _roleService.GetAllPermissionsAsync();
        var permissionIds = permissions.Take(2).Select(p => p.Id).ToList();

        var result = await _roleService.SaveRoleAsync(new SaveRoleRequest(null, "Cashier", "Front-desk role", permissionIds));

        Assert.True(result.IsSuccess);
        Assert.Equal("Cashier", result.Value.Name);
        Assert.Equal(permissionIds.Count, result.Value.PermissionIds.Count);
    }

    [Fact]
    public async Task SaveRoleAsync_EditingExistingRole_UpdatesPermissions()
    {
        var permissions = await _roleService.GetAllPermissionsAsync();
        var created = await _roleService.SaveRoleAsync(
            new SaveRoleRequest(null, "Cashier", null, new[] { permissions[0].Id }));

        var updated = await _roleService.SaveRoleAsync(
            new SaveRoleRequest(created.Value.Id, "Cashier", "Updated", new[] { permissions[0].Id, permissions[1].Id }));

        Assert.True(updated.IsSuccess);
        Assert.Equal(2, updated.Value.PermissionIds.Count);
    }

    [Fact]
    public async Task SaveRoleAsync_WithDuplicateName_Fails()
    {
        await _roleService.SaveRoleAsync(new SaveRoleRequest(null, "Cashier", null, Array.Empty<Guid>()));

        var duplicate = await _roleService.SaveRoleAsync(new SaveRoleRequest(null, "Cashier", null, Array.Empty<Guid>()));

        Assert.True(duplicate.IsFailure);
    }

    [Fact]
    public async Task DeleteRoleAsync_WithNoAssignedUsers_Succeeds()
    {
        var created = await _roleService.SaveRoleAsync(new SaveRoleRequest(null, "Temp Role", null, Array.Empty<Guid>()));

        var result = await _roleService.DeleteRoleAsync(created.Value.Id);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeleteRoleAsync_WithAssignedUser_FailsWithClearError()
    {
        var role = await _roleService.SaveRoleAsync(new SaveRoleRequest(null, "Cashier", null, Array.Empty<Guid>()));
        await _userService.CreateUserAsync(new CreateUserRequest("jdoe", "Password1!", "Jane Doe", null, role.Value.Id));

        var result = await _roleService.DeleteRoleAsync(role.Value.Id);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task GetAllPermissionsAsync_ReturnsSeededPermissions()
    {
        var permissions = await _roleService.GetAllPermissionsAsync();

        Assert.NotEmpty(permissions);
    }

    public void Dispose()
    {
        _context.Dispose();
        if (File.Exists(_databaseFilePath))
        {
            File.Delete(_databaseFilePath);
        }
    }
}
