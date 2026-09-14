namespace InventoryManagement.Domain.Common;

/// <summary>
/// Base class for every domain entity. Uses a GUID key so records can be created
/// offline (client-side) without needing a round-trip to a central sequence -
/// important for an offline-first application that may later need to merge or
/// sync data.
///
/// Deliberately has NO row-version/concurrency-token property. EF Core has a
/// built-in convention that automatically treats any byte[] property named
/// exactly "RowVersion" (or "Timestamp") as an optimistic concurrency token -
/// with zero explicit configuration required to trigger it. That convention
/// assumes the database itself auto-generates a new value on every write,
/// which is how SQL Server's native ROWVERSION type works. SQLite has no
/// such mechanism, so a property with that name would silently turn into a
/// concurrency check that can never actually be satisfied correctly,
/// producing DbUpdateConcurrencyException ("expected to affect 1 row, but
/// affected 0") on updates that are otherwise completely valid. This
/// codebase's actual concurrency-safety strategy (see InventoryService) is
/// deliberate, explicit transaction/locking logic instead.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; internal set; } = Guid.NewGuid();

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
