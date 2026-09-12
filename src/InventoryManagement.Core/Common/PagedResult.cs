namespace InventoryManagement.Core.Common;

/// <summary>
/// A page of results plus enough metadata for the UI to render pagination
/// controls, without ever loading the full result set into memory. Any
/// query-heavy list screen (Products now, Purchases/Sales/etc. later) should
/// return this instead of a bare list.
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int PageNumber, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    public static PagedResult<T> Empty(int pageNumber, int pageSize) =>
        new(Array.Empty<T>(), 0, pageNumber, pageSize);
}
