using InventoryManagement.Application.Common.Interfaces;

namespace InventoryManagement.Infrastructure.Common;

/// <inheritdoc cref="IDateTimeProvider" />
public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
