using BuildingBlocks.Core.DomainModel;

namespace Ordering.Domain.Abstractions;

public interface IRepository<T> where T : IAggregate
{
    public Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    public Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    public Task AddAsync(T entity, CancellationToken cancellationToken = default);
    public void Update(T entity, CancellationToken cancellationToken = default);
    public void Delete(T entity, CancellationToken cancellationToken = default);
}
