namespace Ordering.Infrastructure.Interceptors;

public class AuditableEntityInterceptor : SaveChangesInterceptor
{
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        UpdateEntities(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        UpdateEntities(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    private static void UpdateEntities(DbContext? context)
    {
        if (context == null) return;

        foreach (var entity in context.ChangeTracker.Entries<IEntity>())
        {
            if (entity.State == EntityState.Added)
            {
                entity.Entity.CreatedAt = DateTime.UtcNow;
            }

            if (entity.State == EntityState.Added
                || entity.State == EntityState.Modified
                || entity.HasChangedOwnedEntities())
            {
                entity.Entity.LastModified = DateTime.UtcNow;
                entity.Entity.LastModifiedBy = "system";
            }
        }
    }
}

public static class Extensios
{
    public static bool HasChangedOwnedEntities(this EntityEntry entry) =>
        entry.References.Any(r =>
            r.TargetEntry != null
            && r.TargetEntry.Metadata.IsOwned()
            && (r.TargetEntry.State != EntityState.Added
                || r.TargetEntry.State != EntityState.Modified));
}