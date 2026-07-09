namespace Catalog.Function.Modules.Products.Data;

public interface IProductRepository
{
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Product product, CancellationToken cancellationToken = default);

    // Used by DeleteCategory's orphan-reference guard.
    Task<bool> AnyReferencingCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);
}
