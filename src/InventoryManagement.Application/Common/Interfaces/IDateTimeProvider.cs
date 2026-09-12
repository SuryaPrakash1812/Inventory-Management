namespace InventoryManagement.Application.Common.Interfaces;

/// <summary>
/// Provides the current time. Business logic must never call DateTime.Now/UtcNow
/// directly - going through this interface keeps calculations testable and keeps
/// every timestamp in the system consistently UTC.
/// </summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
