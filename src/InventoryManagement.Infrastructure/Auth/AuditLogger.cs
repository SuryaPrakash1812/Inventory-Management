using InventoryManagement.Application.Auth;
using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Domain.Entities;
using InventoryManagement.Domain.Enums;
using InventoryManagement.Infrastructure.Data;

namespace InventoryManagement.Infrastructure.Auth;

/// <summary>
/// IMPORTANT: <see cref="LogAsync"/> only adds the AuditLog row to the
/// DbContext's tracked changes - it deliberately does NOT call
/// SaveChangesAsync itself. That is the caller's job, so the audit entry
/// commits in the same transaction as whatever it is recording (e.g. a
/// user's LastLoginAtUtc update and their "LoggedIn" audit row either both
/// commit or neither does - never one without the other).
/// </summary>
public sealed class AuditLogger : IAuditLogger
{
    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ICurrentUserSession _session;

    public AuditLogger(IAppDbContext context, IDateTimeProvider dateTimeProvider, ICurrentUserSession session)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
        _session = session;
    }

    public Task LogAsync(
        AuditAction action,
        string entityName,
        Guid? entityId = null,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            OccurredAtUtc = _dateTimeProvider.UtcNow,
            UserId = _session.CurrentUser?.Id,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            Details = details,
        });

        return Task.CompletedTask;
    }
}
