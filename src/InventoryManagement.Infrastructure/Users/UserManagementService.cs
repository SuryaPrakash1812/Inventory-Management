using InventoryManagement.Application.Auth;
using InventoryManagement.Application.Users;
using InventoryManagement.Core.Common;
using InventoryManagement.Domain.Entities;
using InventoryManagement.Domain.Enums;
using InventoryManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Infrastructure.Users;

public sealed class UserManagementService : IUserManagementService
{
    private readonly IAppDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditLogger _auditLogger;
    private readonly ICurrentUserSession _session;

    public UserManagementService(
        IAppDbContext context,
        IPasswordHasher passwordHasher,
        IAuditLogger auditLogger,
        ICurrentUserSession session)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _auditLogger = auditLogger;
        _session = session;
    }

    public async Task<IReadOnlyList<UserSummary>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .Include(u => u.Role)
            .OrderBy(u => u.Username)
            .Select(u => new UserSummary(
                u.Id, u.Username, u.FullName, u.Email, u.RoleId, u.Role!.Name, u.IsActive, u.LastLoginAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<UserSummary>> CreateUserAsync(
        CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        var usernameTaken = await _context.Users
            .AnyAsync(u => u.Username == request.Username, cancellationToken);

        if (usernameTaken)
        {
            return Result.Failure<UserSummary>($"Username '{request.Username}' is already in use.");
        }

        var user = new User
        {
            Username = request.Username,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            FullName = request.FullName,
            Email = request.Email,
            RoleId = request.RoleId,
            IsActive = true,
        };

        _context.Users.Add(user);

        await _auditLogger.LogAsync(
            AuditAction.Created, nameof(User), user.Id, $"User '{user.Username}' created.", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToSummaryAsync(user.Id, cancellationToken));
    }

    public async Task<Result<UserSummary>> UpdateUserAsync(
        UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.SingleOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<UserSummary>("User not found.");
        }

        user.FullName = request.FullName;
        user.Email = request.Email;
        user.RoleId = request.RoleId;
        user.IsActive = request.IsActive;

        await _auditLogger.LogAsync(
            AuditAction.Updated, nameof(User), user.Id, $"User '{user.Username}' updated.", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToSummaryAsync(user.Id, cancellationToken));
    }

    public async Task<Result> ChangePasswordAsync(
        ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.SingleOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure("User not found.");
        }

        user.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);

        await _auditLogger.LogAsync(
            AuditAction.PasswordChanged,
            nameof(User),
            user.Id,
            $"Password changed for user '{user.Username}'.",
            cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result> DeactivateUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (_session.CurrentUser?.Id == userId)
        {
            return Result.Failure("You cannot deactivate your own account while signed in.");
        }

        var user = await _context.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure("User not found.");
        }

        user.IsActive = false;

        await _auditLogger.LogAsync(
            AuditAction.Updated, nameof(User), user.Id, $"User '{user.Username}' deactivated.", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private async Task<UserSummary> ToSummaryAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _context.Users
            .Include(u => u.Role)
            .Where(u => u.Id == userId)
            .Select(u => new UserSummary(
                u.Id, u.Username, u.FullName, u.Email, u.RoleId, u.Role!.Name, u.IsActive, u.LastLoginAtUtc))
            .SingleAsync(cancellationToken);
    }
}
