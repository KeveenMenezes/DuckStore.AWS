namespace Catalog.API.Features.Products.GetProductByCategory;

public record GetProductByCategoryQuery(Guid CategoryId) :
    IQuery<GetProductByCategoryResult>;

public record GetProductByCategoryResult(IEnumerable<Product> Products);

public class GetProductByCategoryQueryHandler
    (IDocumentSession session)
    : IQueryHandler<GetProductByCategoryQuery, GetProductByCategoryResult>
{
    public async Task<GetProductByCategoryResult> Handle(
        GetProductByCategoryQuery request, CancellationToken cancellationToken)
    {
        var products = await session.Query<Product>()
            .Where(p =>
                p.CategoryIds.Contains(CategoryId.Of(request.CategoryId)))
            .ToListAsync(token: cancellationToken);

        return new GetProductByCategoryResult(products);
    }
}
