namespace Basket.API.Basket.StoreBasket;

public record StoreBasketRequest(ShoppingCart Cart);
public record StoreBasketResponse(string UserName);

public class StoreBasketEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
            "/basket",
            async (StoreBasketRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var command = request.Adapt<StoreBasketCommand>();

                var response = await sender.Send(command, cancellationToken);

                return Results.Created($"/basket/{response.UserName}", response);
            }
        )
        .WithName("CreatedProduct")
        .Produces<StoreBasketResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithSummary("Create Product")
        .WithDescription("Create Product");
    }
}



