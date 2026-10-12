using Basket.Function.Shared.Configuration;
namespace Basket.Function.Modules.ShoppingCarts.EventsIntegration.Publishers.Rules;

// Publishes BasketCheckoutEvent for a checkout: the saga's CHECKOUT# record (INSERT, ADR-0050 §4) or,
// while the old pipeline still runs, a cart row CheckoutBasketCommandHandler marked (MODIFY). Each
// checkout writes only one of the two, so nothing is published twice.
//
// Unlike OrderCreatedRule (Ordering), this rule does NOT re-hydrate from the repository: the full
// event payload is already embedded as JSON in the item's CheckoutData attribute, so there is
// nothing to fetch — it only ever reads the new stream image.
public sealed class CheckoutedRule : IStreamRule<ShoppingCartStreamImage>
{
    public bool Match(StreamContext<ShoppingCartStreamImage> context) =>
        context.EventName is "INSERT" or "MODIFY"
        && context.New?.Type == "Checkout"
        && context.New?.CheckoutData is not null;

    public Task<PublishInstruction> BuildAsync(
        StreamContext<ShoppingCartStreamImage> context, CancellationToken cancellationToken = default)
    {
        var checkoutEvent = JsonSerializer.Deserialize(
            context.New!.CheckoutData!, BasketSerializerContext.Default.BasketCheckoutEvent)!;

        return Task.FromResult(new PublishInstruction(nameof(BasketCheckoutEvent), checkoutEvent));
    }
}
