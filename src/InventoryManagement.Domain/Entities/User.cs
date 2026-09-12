using InventoryManagement.Domain.Common;

namespace InventoryManagement.Domain.Entities;

/// <summary>
/// An application login account. Stage 2 only establishes the schema/shape;
/// the actual authentication flow (password hashing, session handling, first-
/// run admin setup) is built in Stage 9. <see cref="PasswordHash"/> is never
/// a plain password - it is expected to hold a salted hash produced by
/// whatever algorithm Stage 9 chooses (e.g. PBKDF2), never written to logs.
/// </summary>
public class User : AuditableSoftDeleteEntity
{
    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public bool IsActive { get; set; } = true;

    public Guid? RoleId { get; set; }

    public Role? Role { get; set; }

    public DateTimeOffset? LastLoginAtUtc { get; set; }
}
