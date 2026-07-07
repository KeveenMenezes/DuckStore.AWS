namespace CatalogView.Function;

public partial class Functions
{
    // Invoked directly by the AppSync `products(...)` Lambda resolver (ADR-0027 amends ADR-0009).
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task<SearchProductsResponse> SearchProducts(
        SearchProductsRequest request,
        [FromServices] SearchProductsHandler handler)
    {
        return await handler.HandleAsync(request);
    }
}
