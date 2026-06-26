using Catalog.Function.Features.Products.DeleteProduct;

namespace Catalog.Function;

public record DeleteProductResponse(bool IsSuccess);

public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    [HttpApi(LambdaHttpMethod.Delete, "/products/{id}")]
    public async Task<DeleteProductResponse> DeleteProduct(
        Guid id,
        [FromServices] ISender sender)
    {
        var result = await sender.Send(new DeleteProductCommand(id), CancellationToken.None);

        return result.Adapt<DeleteProductResponse>();
    }
}
