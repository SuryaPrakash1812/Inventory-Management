namespace InventoryManagement.Application.Users;

public sealed record UserSummary(
    Guid Id,
    string Username,
    string FullName,
    string? Email,
    Guid? RoleId,
    string? RoleName,
    bool IsActive,
    DateTimeOffset? LastLoginAtUtc);

public sealed record CreateUserRequest(
    string Username,
    string Password,
    string FullName,
    string? Email,
    Guid? RoleId);

public sealed record UpdateUserRequest(
    Guid UserId,
    string FullName,
    string? Email,
    Guid? RoleId,
    bool IsActive);

public sealed record ChangePasswordRequest(Guid UserId, string NewPassword);
