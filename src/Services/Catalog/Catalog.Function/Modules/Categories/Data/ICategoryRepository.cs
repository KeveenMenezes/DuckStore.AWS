namespace Catalog.Function.Modules.Categories.Data;

public interface ICategoryRepository
{
    Task AddAsync(Category category, CancellationToken cancellationToken = default);
}
