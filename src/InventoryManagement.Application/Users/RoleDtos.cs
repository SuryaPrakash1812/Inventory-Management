namespace InventoryManagement.Application.Users;

public sealed record PermissionInfo(Guid Id, string Name, string? Description);

public sealed record RoleSummary(
    Guid Id,
    string Name,
    string? Description,
    int UserCount,
    IReadOnlyList<Guid> PermissionIds);

public sealed record SaveRoleRequest(
    Guid? RoleId,
    string Name,
    string? Description,
    IReadOnlyList<Guid> PermissionIds);
