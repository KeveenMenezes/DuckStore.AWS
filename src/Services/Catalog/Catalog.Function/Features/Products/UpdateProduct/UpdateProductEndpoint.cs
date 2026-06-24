using Catalog.API.Features.Products.UpdateProduct;

namespace Catalog.Function;

public record UpdateProductRequest(
    Guid Id,
    string Name,
    string Description,
    string ImageUrl,
    decimal Price,
    int Stock,
    List<Guid> CategoryIds);

public record UpdateProductResponse(Guid Id);

public partial class Functions
{
    [LambdaFunction]
    [HttpApi(LambdaHttpMethod.Put, "/products")]
    public async Task<IHttpResult> UpdateProduct(
        [FromBody] UpdateProductRequest request,
        [FromServices] ISender sender)
    {
        try
        {
            var command = request.Adapt<UpdateProductCommand>();

            var result = await sender.Send(command, CancellationToken.None);

            return HttpResults.Ok(new UpdateProductResponse(result.Id));
        }
        catch (ProductNotFoundException)
        {
            return HttpResults.NotFound();
        }
    }
}