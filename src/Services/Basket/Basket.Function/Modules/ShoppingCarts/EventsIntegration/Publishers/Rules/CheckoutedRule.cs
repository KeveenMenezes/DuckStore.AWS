using Basket.Function.Shared.Configuration;
namespace Basket.Function.Modules.ShoppingCarts.EventsIntegration.Publishers.Rules;

// Publishes BasketCheckoutEvent when the checkout saga's close inserts a CHECKOUT# record (ADR-0050 §4).
// The record exists at most once per attempt, so each checkout is published once.
//
// Unlike OrderCreatedRule (Ordering), this rule does NOT re-hydrate from the repository: the full
// event payload is already embedded as JSON in the item's CheckoutData attribute, so there is
// nothing to fetch — it only ever reads the new stream image.
public sealed class CheckoutedRule : IStreamRule<ShoppingCartStreamImage>
{
    public bool Match(StreamContext<ShoppingCartStreamImage> context) =>
        context.EventName == "INSERT"
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
