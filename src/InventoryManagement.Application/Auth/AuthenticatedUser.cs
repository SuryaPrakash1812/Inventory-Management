namespace InventoryManagement.Application.Auth;

/// <summary>
/// Everything the rest of the app needs to know about the currently signed-in
/// user, captured once at login. Permissions are resolved to a flat set of
/// names at sign-in time (rather than re-querying Role -&gt; Permissions on
/// every check) so authorization checks are simple, synchronous, and cheap.
/// </summary>
public sealed class AuthenticatedUser
{
    public required Guid Id { get; init; }

    public required string Username { get; init; }

    public required string FullName { get; init; }

    public Guid? RoleId { get; init; }

    public string? RoleName { get; init; }

    public required IReadOnlySet<string> Permissions { get; init; }

    public bool HasPermission(string permissionName) => Permissions.Contains(permissionName);
}
