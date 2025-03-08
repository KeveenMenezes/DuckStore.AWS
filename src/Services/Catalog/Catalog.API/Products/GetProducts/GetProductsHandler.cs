namespace Catalog.API.Products.GetProducts;

public record GetProductsQuery(int PageIndex, int PageSize)
    : IQuery<GetProductsResult>;

public record GetProductsResult(IEnumerable<Product> Products);

internal class GetProductsQueryHandler(IDocumentSession session)
    : IQueryHandler<GetProductsQuery, GetProductsResult>
{
    public async Task<GetProductsResult> Handle(
        GetProductsQuery request, CancellationToken cancellationToken)
    {
        var products = await session.Query<Product>()
            .ToPagedListAsync(
                request.PageIndex,
                request.PageSize,
                cancellationToken);

        return new GetProductsResult(products);
    }
}
