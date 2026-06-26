namespace Catalog.Function.Features.Products.GetProductByCategory;

public record GetProductByCategoryQuery(Guid CategoryId) :
    IQuery<GetProductByCategoryResult>;

public record GetProductByCategoryResult(IEnumerable<Product> Products);

public class GetProductByCategoryQueryHandler
    (IProductRepository productRepository)
    : IQueryHandler<GetProductByCategoryQuery, GetProductByCategoryResult>
{
    public async Task<GetProductByCategoryResult> Handle(
        GetProductByCategoryQuery request, CancellationToken cancellationToken)
    {
        var products = await productRepository.GetByCategoryAsync(request.CategoryId, cancellationToken);

        return new GetProductByCategoryResult(products);
    }
}
