namespace Catalog.Function.Modules.Products.Data;

public interface IProductRepository
{
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Product product, CancellationToken cancellationToken = default);
}
