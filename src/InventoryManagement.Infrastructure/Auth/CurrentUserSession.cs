using InventoryManagement.Application.Auth;

namespace InventoryManagement.Infrastructure.Auth;

public sealed class CurrentUserSession : ICurrentUserSession
{
    public AuthenticatedUser? CurrentUser { get; private set; }

    public bool IsAuthenticated => CurrentUser is not null;

    public event EventHandler? CurrentUserChanged;

    public void SignIn(AuthenticatedUser user)
    {
        CurrentUser = user;
        CurrentUserChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut()
    {
        CurrentUser = null;
        CurrentUserChanged?.Invoke(this, EventArgs.Empty);
    }
}
