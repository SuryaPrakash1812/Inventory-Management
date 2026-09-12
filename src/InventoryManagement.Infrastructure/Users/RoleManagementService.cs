using InventoryManagement.Application.Auth;
using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Application.Users;
using InventoryManagement.Core.Common;
using InventoryManagement.Domain.Entities;
using InventoryManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Infrastructure.Users;

public sealed class RoleManagementService : IRoleManagementService
{
    private readonly IAppDbContext _context;
    private readonly IAuditLogger _auditLogger;
    private readonly ICurrentUserSession _session;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RoleManagementService(
        IAppDbContext context,
        IAuditLogger auditLogger,
        ICurrentUserSession session,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _auditLogger = auditLogger;
        _session = session;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<IReadOnlyList<PermissionInfo>> GetAllPermissionsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Permissions
            .OrderBy(p => p.Name)
            .Select(p => new PermissionInfo(p.Id, p.Name, p.Description))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RoleSummary>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        var roles = await _context.Roles
            .Include(r => r.Permissions)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);

        var userCounts = await _context.Users
            .Where(u => u.RoleId != null)
            .GroupBy(u => u.RoleId)
            .Select(g => new { RoleId = g.Key!.Value, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count, cancellationToken);

        return roles
            .Select(r => new RoleSummary(
                r.Id,
                r.Name,
                r.Description,
                userCounts.GetValueOrDefault(r.Id),
                r.Permissions.Select(p => p.Id).ToList()))
            .ToList();
    }

    public async Task<Result<RoleSummary>> SaveRoleAsync(
        SaveRoleRequest request, CancellationToken cancellationToken = default)
    {
        Role role;
        AuditAction auditAction;

        if (request.RoleId is { } existingId)
        {
            var existing = await _context.Roles
                .Include(r => r.Permissions)
                .SingleOrDefaultAsync(r => r.Id == existingId, cancellationToken);

            if (existing is null)
            {
                return Result.Failure<RoleSummary>("Role not found.");
            }

            role = existing;
            auditAction = AuditAction.Updated;
        }
        else
        {
            var nameTaken = await _context.Roles.AnyAsync(r => r.Name == request.Name, cancellationToken);
            if (nameTaken)
            {
                return Result.Failure<RoleSummary>($"A role named '{request.Name}' already exists.");
            }

            role = new Role();
            _context.Roles.Add(role);
            auditAction = AuditAction.Created;
        }

        role.Name = request.Name;
        role.Description = request.Description;

        var selectedPermissions = await _context.Permissions
            .Where(p => request.PermissionIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        role.Permissions.Clear();
        foreach (var permission in selectedPermissions)
        {
            role.Permissions.Add(permission);
        }

        await _auditLogger.LogAsync(
            auditAction,
            nameof(Role),
            role.Id,
            $"Role '{role.Name}' saved with {selectedPermissions.Count} permission(s).",
            cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToSummaryAsync(role.Id, cancellationToken));
    }

    public async Task<Result> DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        var hasUsers = await _context.Users.AnyAsync(u => u.RoleId == roleId, cancellationToken);
        if (hasUsers)
        {
            return Result.Failure("Cannot delete a role while users are still assigned to it. Reassign them first.");
        }

        var role = await _context.Roles.SingleOrDefaultAsync(r => r.Id == roleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure("Role not found.");
        }

        role.MarkDeleted(_dateTimeProvider.UtcNow, _session.CurrentUser?.Id);

        await _auditLogger.LogAsync(
            AuditAction.Deleted, nameof(Role), role.Id, $"Role '{role.Name}' deleted.", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private async Task<RoleSummary> ToSummaryAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var role = await _context.Roles
            .Include(r => r.Permissions)
            .SingleAsync(r => r.Id == roleId, cancellationToken);

        var userCount = await _context.Users.CountAsync(u => u.RoleId == roleId, cancellationToken);

        return new RoleSummary(
            role.Id, role.Name, role.Description, userCount, role.Permissions.Select(p => p.Id).ToList());
    }
}
