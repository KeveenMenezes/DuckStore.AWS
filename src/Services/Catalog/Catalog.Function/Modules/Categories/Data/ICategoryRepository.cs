namespace Catalog.Function.Modules.Categories.Data;

public interface ICategoryRepository
{
    Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<List<Category>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    Task<List<Category>> ListAsync(CancellationToken cancellationToken = default);

    Task<List<Category>> ListChildrenAsync(Guid parentId, CancellationToken cancellationToken = default);

    // Every category whose materialized Path contains id — used for the Move cycle guard and to
    // cascade a reparented ancestor's Path onto its existing descendants.
    Task<List<Category>> ListDescendantsAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(Category category, CancellationToken cancellationToken = default);

    Task UpdateAsync(Category category, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
