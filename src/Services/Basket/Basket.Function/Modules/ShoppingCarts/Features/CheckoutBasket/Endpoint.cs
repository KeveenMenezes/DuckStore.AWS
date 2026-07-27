using Basket.Function.Modules.ShoppingCarts.Features.CheckoutBasket;

namespace Basket.Function;

public record CheckoutBasketRequest(BasketCheckoutDto BasketCheckoutDto);
public record CheckoutBasketResponse(bool IsSuccess);

// Triggered via a Lambda Function URL. Marks a cart as checked out; talks directly to DynamoDB
// (no cache). The DynamoDB Streams publisher on shopping-carts picks up the Checkout marker and
// publishes BasketCheckoutEvent (CDC pattern, ADR-0005).
public partial class Functions
{
    [LambdaFunction]
    public async Task<CheckoutBasketResponse> CheckoutBasket(
        CheckoutBasketRequest request,
        [FromServices] ISender sender)
    {
        var command = new CheckoutBasketCommand(request.BasketCheckoutDto);
        var result = await sender.Send(command, CancellationToken.None);
        return new CheckoutBasketResponse(result.IsSuccess);
    }
}
