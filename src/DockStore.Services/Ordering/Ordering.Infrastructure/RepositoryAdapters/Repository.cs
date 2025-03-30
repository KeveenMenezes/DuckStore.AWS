using Ordering.Domain.Abstractions.Repositories;

namespace Ordering.Infrastructure.RepositoryAdapters;

public class Repository<T>(DbContext db)
    : IRepository<T> where T : class, IAggregate
{
    private readonly DbContext _db = db;
    private DbSet<T> Entity => _db.Set<T>();

    public async Task AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        await Entity.AddAsync(entity, cancellationToken);
        await SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        Entity.Update(entity);
        await SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(T entity, CancellationToken cancellationToken = default)
    {
        Entity.Remove(entity);
        await SaveChangesAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _db.SaveChangesAsync(cancellationToken);
    }
}