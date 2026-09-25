using InventoryManagement.Application.Auth;
using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Core.Common;
using InventoryManagement.Domain.Entities;
using InventoryManagement.Domain.Enums;
using InventoryManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Infrastructure.Auth;

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly IAppDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICurrentUserSession _session;
    private readonly IAuditLogger _auditLogger;
    private readonly IDateTimeProvider _dateTimeProvider;

    public AuthenticationService(
        IAppDbContext context,
        IPasswordHasher passwordHasher,
        ICurrentUserSession session,
        IAuditLogger auditLogger,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _session = session;
        _auditLogger = auditLogger;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<bool> IsFirstRunAsync(CancellationToken cancellationToken = default) =>
        !await _context.Users.AnyAsync(cancellationToken);

    public async Task<Result<AuthenticatedUser>> LoginAsync(
        string username, string password, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .ThenInclude(r => r!.Permissions)
            .SingleOrDefaultAsync(u => u.Username == username, cancellationToken);

        // Deliberately the same error for "no such user" and "wrong password" -
        // distinguishing them would let an attacker enumerate valid usernames.
        if (user is null || !_passwordHasher.VerifyPassword(password, user.PasswordHash))
        {
            return Result.Failure<AuthenticatedUser>("Invalid username or password.");
        }

        if (!user.IsActive)
        {
            return Result.Failure<AuthenticatedUser>("This account has been deactivated.");
        }

        user.LastLoginAtUtc = _dateTimeProvider.UtcNow;

        await _auditLogger.LogAsync(
            AuditAction.LoggedIn, nameof(User), user.Id, $"User '{user.Username}' logged in.", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        var authenticatedUser = ToAuthenticatedUser(user);
        _session.SignIn(authenticatedUser);

        return Result.Success(authenticatedUser);
    }

    public async Task<Result<AuthenticatedUser>> CreateFirstAdministratorAsync(
        string username, string password, string fullName, CancellationToken cancellationToken = default)
    {
        if (!await IsFirstRunAsync(cancellationToken))
        {
            return Result.Failure<AuthenticatedUser>("Setup has already been completed.");
        }

        var administratorRole = await _context.Roles
            .Include(r => r.Permissions)
            .SingleOrDefaultAsync(r => r.Name == "Administrator", cancellationToken);

        if (administratorRole is null)
        {
            return Result.Failure<AuthenticatedUser>("The Administrator role is missing from the database.");
        }

        var user = new User
        {
            Username = username,
            PasswordHash = _passwordHasher.HashPassword(password),
            FullName = fullName,
            RoleId = administratorRole.Id,
            Role = administratorRole,
            IsActive = true,
            LastLoginAtUtc = _dateTimeProvider.UtcNow,
        };

        _context.Users.Add(user);

        await _auditLogger.LogAsync(
            AuditAction.Created,
            nameof(User),
            user.Id,
            $"First-run administrator account '{user.Username}' created.",
            cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        var authenticatedUser = ToAuthenticatedUser(user);
        _session.SignIn(authenticatedUser);

        return Result.Success(authenticatedUser);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var user = _session.CurrentUser;

        if (user is not null)
        {
            await _auditLogger.LogAsync(
                AuditAction.LoggedOut, nameof(User), user.Id, $"User '{user.Username}' logged out.", cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }

        _session.SignOut();
    }

    private static AuthenticatedUser ToAuthenticatedUser(User user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        FullName = user.FullName,
        RoleId = user.RoleId,
        RoleName = user.Role?.Name,
        Permissions = user.Role?.Permissions.Select(p => p.Name).ToHashSet() ?? new HashSet<string>(),
    };
}
