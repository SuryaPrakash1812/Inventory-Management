using InventoryManagement.Core.Common;

namespace InventoryManagement.Application.Users;

public interface IUserManagementService
{
    Task<IReadOnlyList<UserSummary>> GetUsersAsync(CancellationToken cancellationToken = default);

    Task<Result<UserSummary>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

    Task<Result<UserSummary>> UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken = default);

    Task<Result> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the user's IsActive flag to false so they can no longer log in.
    /// Reversible via <see cref="UpdateUserAsync"/> - this deliberately does
    /// not soft-delete the account, so the user stays visible (and easy to
    /// reactivate) in the user list rather than disappearing entirely.
    /// </summary>
    Task<Result> DeactivateUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
