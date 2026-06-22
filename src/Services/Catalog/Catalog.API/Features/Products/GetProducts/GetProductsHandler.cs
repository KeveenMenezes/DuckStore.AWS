namespace Catalog.API.Features.Products.GetProducts;

public record GetProductsQuery(int PageIndex, int PageSize)
    : IQuery<GetProductsResult>;

public record GetProductsResult(PaginatedResult<Product> PaginatedProducts);

public class GetProductsQueryHandler(IProductRepository productRepository)
    : IQueryHandler<GetProductsQuery, GetProductsResult>
{
    public async Task<GetProductsResult> Handle(
        GetProductsQuery request, CancellationToken cancellationToken)
    {
        var pagedProducts = await productRepository.GetPagedAsync(
            request.PageIndex, request.PageSize, cancellationToken);

        return new GetProductsResult(pagedProducts);
    }
}
