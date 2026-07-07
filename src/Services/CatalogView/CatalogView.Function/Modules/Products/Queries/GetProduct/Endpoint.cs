namespace CatalogView.Function;

public partial class Functions
{
    // Invoked directly by the AppSync `product(id)` Lambda resolver (ADR-0027 amends ADR-0009).
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task<GetProductResponse?> GetProduct(
        GetProductRequest request,
        [FromServices] GetProductHandler handler)
    {
        return await handler.HandleAsync(request);
    }
}
