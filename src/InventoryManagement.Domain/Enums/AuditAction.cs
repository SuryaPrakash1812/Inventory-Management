namespace InventoryManagement.Domain.Enums;

/// <summary>The kind of event an <see cref="Entities.AuditLog"/> row records.</summary>
public enum AuditAction
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
    LoggedIn = 3,
    LoggedOut = 4,
    PasswordChanged = 5,
    Other = 6,
}
