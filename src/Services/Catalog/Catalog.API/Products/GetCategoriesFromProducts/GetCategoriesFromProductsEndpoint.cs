using System;

namespace Catalog.API.Products.GetCategoriesFromProducts;

public class GetCategoriesFromProductsEndpoint
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/products/categories",
            async (ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetCategoriesFromProductsQuery(), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetCategoriesFromProducts")
        .Produces<GetCategoriesFromProductsQuery>(StatusCodes.Status200OK)
        .WithSummary("Get categories from products")
        .WithDescription("Retrieves all unique categories from products");
    }
}
