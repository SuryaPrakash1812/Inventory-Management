namespace InventoryManagement.Domain.Common;

/// <summary>
/// Base class for every domain entity. Uses a GUID key so records can be created
/// offline (client-side) without needing a round-trip to a central sequence -
/// important for an offline-first application that may later need to merge or
/// sync data.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    /// <summary>
    /// Row version used by EF Core for optimistic concurrency control, preventing
    /// silent overwrites when the same record is edited from two places.
    /// </summary>
    public byte[]? RowVersion { get; protected set; }

    public override bool Equals(object? obj)
    {
        if (obj is not BaseEntity other)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (GetType() != other.GetType())
        {
            return false;
        }

        return Id == other.Id;
    }

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
