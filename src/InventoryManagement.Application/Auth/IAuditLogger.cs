using InventoryManagement.Domain.Enums;

namespace InventoryManagement.Application.Auth;

/// <summary>
/// Writes an <see cref="Domain.Entities.AuditLog"/> row. Kept as a thin,
/// single-purpose abstraction so every service that needs to record "who did
/// what, when" calls the same code path instead of constructing AuditLog
/// rows by hand in a dozen places.
/// </summary>
public interface IAuditLogger
{
    Task LogAsync(
        AuditAction action,
        string entityName,
        Guid? entityId = null,
        string? details = null,
        CancellationToken cancellationToken = default);
}
