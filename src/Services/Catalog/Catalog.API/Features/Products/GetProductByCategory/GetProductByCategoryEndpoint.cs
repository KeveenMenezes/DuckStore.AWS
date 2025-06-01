namespace Catalog.API.Features.Products.GetProductByCategory;

public record GetProductByCategoryResponse(IEnumerable<Product> Products);

public class GetProductByCategoryEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/products/category/{categoryId}",
            async (Guid categoryId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new GetProductByCategoryQuery(categoryId), cancellationToken);

            var response = result.Adapt<GetProductByCategoryResponse>();
            return Results.Ok(response);
        })
        .WithName("GetProductByCategory")
        .Produces<GetProductByCategoryResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Get products by category")
        .WithDescription("Retrieves products by category");
    }
}



