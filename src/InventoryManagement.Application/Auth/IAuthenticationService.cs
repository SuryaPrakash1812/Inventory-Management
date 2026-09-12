using InventoryManagement.Core.Common;

namespace InventoryManagement.Application.Auth;

/// <summary>
/// Handles the login/logout/first-run-setup flows. Everything here works
/// entirely offline against the local database - there is no cloud
/// authentication of any kind.
/// </summary>
public interface IAuthenticationService
{
    /// <summary>True if no user accounts exist yet, meaning first-run setup should run instead of login.</summary>
    Task<bool> IsFirstRunAsync(CancellationToken cancellationToken = default);

    Task<Result<AuthenticatedUser>> LoginAsync(
        string username, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the very first user account (assigned the built-in
    /// Administrator role) and signs them in. Only valid when
    /// <see cref="IsFirstRunAsync"/> is true - callers should not offer this
    /// as an option otherwise.
    /// </summary>
    Task<Result<AuthenticatedUser>> CreateFirstAdministratorAsync(
        string username, string password, string fullName, CancellationToken cancellationToken = default);

    Task LogoutAsync(CancellationToken cancellationToken = default);
}
