using Catalog.API.Features.Products.GetProductByCategory;

namespace Catalog.Function;

public record GetProductByCategoryResponse(IEnumerable<Product> Products);

public partial class Functions
{
    [LambdaFunction]
    [HttpApi(LambdaHttpMethod.Get, "/products/category/{categoryId}")]
    public async Task<GetProductByCategoryResponse> GetProductByCategory(
        Guid categoryId,
        [FromServices] ISender sender)
    {
        var result = await sender.Send(new GetProductByCategoryQuery(categoryId), CancellationToken.None);

        return result.Adapt<GetProductByCategoryResponse>();
    }
}