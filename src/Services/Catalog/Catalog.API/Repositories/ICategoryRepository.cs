namespace Catalog.API.Repositories;

public interface ICategoryRepository
{
    Task<PaginatedResult<Category>> GetPagedAsync(int pageIndex, int pageSize, CancellationToken cancellationToken = default);
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Category category, CancellationToken cancellationToken = default);
}
