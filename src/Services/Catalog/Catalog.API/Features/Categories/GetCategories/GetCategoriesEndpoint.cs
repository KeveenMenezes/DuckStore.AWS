namespace Catalog.API.Features.Categories.GetCategories;

public record GetCategoriesRequest(int? PageIndex = 1, int? PageSize = 10);

public record GetPaginatedCategoriesResponse(
    PaginatedResult<Category> PaginatedCategories);

public class GetCategoriesEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/categories",
            async (
                [AsParameters] GetCategoriesRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
        {
            var query = request.Adapt<GetCategoriesQuery>();

            var result = await sender.Send(query, cancellationToken);

            var response = result.Adapt<GetPaginatedCategoriesResponse>();

            return Results.Ok(response.PaginatedCategories);
        })
        .WithName("GetCategories")
        .Produces<PaginatedResult<Category>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithSummary("Get Categories")
        .WithDescription("Get all categories");
    }
}
