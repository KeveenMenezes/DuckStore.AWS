namespace Catalog.API.Products.GetProducts;

public record GetProductsRequest(int? PageIndex = 1, int? PageSize = 10);

public record GetPagitationProductsResponse(
    IEnumerable<Product> Products);

public class GetProductsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/products",
            async (
                [AsParameters] GetProductsRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
        {
            var query = request.Adapt<GetProductsQuery>();

            var result = await sender.Send(query, cancellationToken);

            var response = result.Adapt<GetPagitationProductsResponse>();

            return Results.Ok(response.Products);
        })
        .WithName("GetProducts")
        .Produces<GetPagitationProductsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithSummary("Get Products")
        .WithDescription("Get all products");
    }
}



