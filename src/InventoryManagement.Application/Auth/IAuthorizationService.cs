namespace InventoryManagement.Application.Auth;

/// <summary>
/// The single place the UI (or any service) asks whether the current user is
/// allowed to do something. Nothing else should compare role names or
/// permission strings directly - always go through this, so authorization
/// rules stay in one place instead of being scattered across every page.
/// </summary>
public interface IAuthorizationService
{
    bool HasPermission(string permissionName);
}
