namespace Catalog.API.Products.GetProducts;

public record GetProductsQuery(int PageIndex, int PageSize)
    : IQuery<GetProductsResult>;

public record GetProductsResult(PaginatedResult<Product> PaginatedProducts);

public class GetProductsQueryHandler(IDocumentSession session)
    : IQueryHandler<GetProductsQuery, GetProductsResult>
{
    public async Task<GetProductsResult> Handle(
        GetProductsQuery request, CancellationToken cancellationToken)
    {
        var pagedList = await session
            .Query<Product>()
            .ToPagedListAsync(
                request.PageIndex,
                request.PageSize,
                cancellationToken);

        return new GetProductsResult(
            new PaginatedResult<Product>(
                pagedList.Count,
                pagedList.PageSize,
                pagedList.TotalItemCount,
                pagedList.PageNumber,
                pagedList.AsEnumerable())
        );
    }
}
