using Basket.Function.Modules.ShoppingCarts.Features.CheckoutBasket;

namespace Basket.Function;

// From the checkout saga (ADR-0050 §4): CheckoutId and CartVersion select the atomic, idempotent
// close, and BasketCheckoutDto already carries the saga-derived OrderId and the lines priced by
// Pricing and named by Catalog. Without CheckoutId it is the old pipeline's request, accepted only
// while the saga rolls out (ADR-0050 §3).
public record CheckoutBasketRequest(
    BasketCheckoutDto BasketCheckoutDto, string? CheckoutId = null, string? CartVersion = null);

// ErrorType carries a typed refusal as data (CheckoutRefusals) rather than a thrown error, so the
// saga can tell "the cart changed" from an ambiguous failure it must never compensate (ADR-0050 §1).
public record CheckoutBasketResponse(bool IsSuccess, Guid? OrderId, string? ErrorType = null);

public static class CheckoutRefusals
{
    public const string CartChanged = "CartChanged";
}

// Invoked only by the checkout saga (ADR-0050 §2): closes the cart in one conditional transaction
// and talks directly to DynamoDB (no cache). The DynamoDB Streams publisher on shopping-carts picks
// up the CHECKOUT# record and publishes BasketCheckoutEvent (CDC pattern, ADR-0005).
public partial class Functions
{
    [LambdaFunction]
    public async Task<CheckoutBasketResponse> CheckoutBasket(
        CheckoutBasketRequest request,
        [FromServices] ISender sender)
    {
        var command = new CheckoutBasketCommand(request.BasketCheckoutDto, request.CheckoutId, request.CartVersion);
        var result = await sender.Send(command, CancellationToken.None);
        return new CheckoutBasketResponse(result.IsSuccess, result.OrderId, result.ErrorType);
    }
}
