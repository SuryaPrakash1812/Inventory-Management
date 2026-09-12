namespace InventoryManagement.Application.Auth;

/// <summary>
/// Holds the currently signed-in user for as long as the app is running.
/// There is no server-side session/token here - this is a desktop app, so
/// "session" just means "in-memory state that lasts until logout or the
/// process exits". Registered as a singleton; every part of the app reads
/// the same instance.
/// </summary>
public interface ICurrentUserSession
{
    AuthenticatedUser? CurrentUser { get; }

    bool IsAuthenticated { get; }

    /// <summary>Raised whenever <see cref="CurrentUser"/> changes (sign-in or sign-out), so the UI can react.</summary>
    event EventHandler? CurrentUserChanged;

    void SignIn(AuthenticatedUser user);

    void SignOut();
}
