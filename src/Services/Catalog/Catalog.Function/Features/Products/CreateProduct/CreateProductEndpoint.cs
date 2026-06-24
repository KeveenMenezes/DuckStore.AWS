using Catalog.API.Features.Products.CreateProduct;

namespace Catalog.Function;

public record CreateProductRequest(
    string Name,
    string Description,
    string ImageUrl,
    decimal Price,
    int Stock,
    List<Guid> CategoryIds);

public record CreateProductResponse(Guid Id);

public partial class Functions
{
    [LambdaFunction]
    [HttpApi(LambdaHttpMethod.Post, "/products")]
    public async Task<IHttpResult> CreateProduct(
        [FromBody] CreateProductRequest request,
        [FromServices] ISender sender)
    {
        var command = request.Adapt<CreateProductCommand>();

        var result = await sender.Send(command, CancellationToken.None);

        var response = result.Adapt<CreateProductResponse>();

        return HttpResults.Created($"/products/{response.Id}", response);
    }
}