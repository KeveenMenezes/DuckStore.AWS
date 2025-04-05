
namespace Catalog.API.Products.UpdateProduct;

public record UpdateProductRequest(
    Guid Id,
    string Name,
    string Description,
    string ImageUrl,
    decimal Price,
    List<string> Categories);

public record UpdateProductResponse(Guid Id);

public class UpdateProductEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut(
            "/products",
            async (
                UpdateProductRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
        {
            var command = request.Adapt<UpdateProductCommand>();

            var result = await sender.Send(command, cancellationToken);

            return new UpdateProductResponse(result.Id);
        })
        .Produces<UpdateProductResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithSummary("Update product")
        .WithDescription("Update product");
    }
}



