using InventoryManagement.Application.Auth;

namespace InventoryManagement.Infrastructure.Auth;

public sealed class AuthorizationService : IAuthorizationService
{
    private readonly ICurrentUserSession _session;

    public AuthorizationService(ICurrentUserSession session)
    {
        _session = session;
    }

    public bool HasPermission(string permissionName) =>
        _session.CurrentUser?.HasPermission(permissionName) ?? false;
}
