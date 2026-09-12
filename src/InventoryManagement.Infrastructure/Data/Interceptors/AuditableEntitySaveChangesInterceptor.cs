using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace InventoryManagement.Infrastructure.Data.Interceptors;

/// <summary>
/// Stamps CreatedAtUtc/ModifiedAtUtc on every <see cref="AuditableEntity"/>
/// on every save, so individual services never have to remember to do it
/// themselves (and can't forget to, or get it wrong with local vs. UTC time).
///
/// CreatedByUserId/ModifiedByUserId are intentionally left alone here: there
/// is no "current user" concept until Stage 9 (Users/Auth) introduces one.
/// Once it exists, this interceptor is the natural place to populate those
/// fields too, rather than adding a second interceptor.
/// </summary>
public sealed class AuditableEntitySaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IDateTimeProvider _dateTimeProvider;

    public AuditableEntitySaveChangesInterceptor(IDateTimeProvider dateTimeProvider)
    {
        _dateTimeProvider = dateTimeProvider;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        StampAuditFields(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        StampAuditFields(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void StampAuditFields(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = _dateTimeProvider.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.ModifiedAtUtc = now;
                    break;
            }
        }
    }
}
