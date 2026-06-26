using Catalog.Function.Features.Products.GetProducts;

namespace Catalog.Function;

public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    [HttpApi(LambdaHttpMethod.Get, "/products")]
    public async Task<PaginatedResult<Product>> GetProducts(
        [FromServices] ISender sender,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 10)
    {
        var result = await sender.Send(new GetProductsQuery(pageIndex, pageSize), CancellationToken.None);

        return result.PaginatedProducts;
    }
}
