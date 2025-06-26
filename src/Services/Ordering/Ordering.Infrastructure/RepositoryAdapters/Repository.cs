using BuildingBlocks.Core.DomainModel;
using Ordering.Domain.Abstractions;

namespace Ordering.Infrastructure.RepositoryAdapters;

public class Repository<T>(ApplicationDbContext db)
    : IRepository<T> where T : class, IAggregate
{
    private readonly ApplicationDbContext _db = db;
    private DbSet<T> Entity => _db.Set<T>();

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await Entity.FindAsync([id], cancellationToken);
    }

    public async Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await Entity.FindAsync([id], cancellationToken);
    }

    public async Task AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        await Entity.AddAsync(entity, cancellationToken);
    }

    public void Update(T entity, CancellationToken cancellationToken = default)
    {
        Entity.Update(entity);
    }

    public void Delete(T entity, CancellationToken cancellationToken = default)
    {
        Entity.Remove(entity);
    }
}
