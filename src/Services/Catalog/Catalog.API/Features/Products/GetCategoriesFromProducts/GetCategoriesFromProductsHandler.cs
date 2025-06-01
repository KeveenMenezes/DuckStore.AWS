namespace Catalog.API.Products.GetCategoriesFromProducts;

public record GetCategoriesFromProductsQuery()
    : IQuery<GetCategoriesFromProductsResult>;

public record GetCategoriesFromProductsResult(IEnumerable<string> Categories);

public class GetCategoriesFromProductsHandler
    (IDocumentSession session)
    : IQueryHandler<GetCategoriesFromProductsQuery, GetCategoriesFromProductsResult>
{
    public async Task<GetCategoriesFromProductsResult> Handle(
        GetCategoriesFromProductsQuery request, CancellationToken cancellationToken)
    {
        var categories = await session.Query<Product>()
            .SelectMany(p => p.Categories)
            .Distinct()
            .ToListAsync(token: cancellationToken);

        return new GetCategoriesFromProductsResult(categories);
    }
}
