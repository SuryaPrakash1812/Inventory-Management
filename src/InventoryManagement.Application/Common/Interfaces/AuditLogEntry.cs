using InventoryManagement.Domain.Enums;

namespace InventoryManagement.Application.Common.Interfaces;

/// <summary>One row of an entity's audit history, as shown on a "History" tab/view.</summary>
public sealed record AuditLogEntry(
    DateTimeOffset OccurredAtUtc,
    AuditAction Action,
    string? Details);
