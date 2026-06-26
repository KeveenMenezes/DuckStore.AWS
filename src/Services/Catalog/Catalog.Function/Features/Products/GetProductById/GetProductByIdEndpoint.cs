using Catalog.Function.Features.Products.GetProductById;

namespace Catalog.Function;

public record GetProductByIdResponse(Product Product);

public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    [HttpApi(LambdaHttpMethod.Get, "/products/{id}")]
    public async Task<IHttpResult> GetProductById(
        Guid id,
        [FromServices] ISender sender)
    {
        try
        {
            var result = await sender.Send(new GetProductByIdQuery(id), CancellationToken.None);

            var response = result.Adapt<GetProductByIdResponse>();

            return HttpResults.Ok(response);
        }
        catch (ProductNotFoundException)
        {
            return HttpResults.NotFound();
        }
    }
}
