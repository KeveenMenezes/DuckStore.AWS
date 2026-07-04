namespace Basket.Function.Modules.ShoppingCarts.EventsIntegration.Publishers.Rules;

// Publishes BasketCheckoutEvent when CheckoutBasketCommandHandler marks a cart row as checked
// out. Unlike OrderCreatedRule (Ordering), this rule does NOT re-hydrate from the repository:
// the full event payload is already embedded as JSON in the row's CheckoutData attribute, so
// there is nothing to fetch — it only ever reads the new stream image.
public sealed class CheckoutedRule : IStreamRule<ShoppingCartStreamImage>
{
    public bool Match(StreamContext<ShoppingCartStreamImage> context) =>
        context.EventName == "MODIFY" && context.New?.Type == "Checkout" && context.New?.CheckoutData is not null;

    public Task<PublishInstruction> BuildAsync(
        StreamContext<ShoppingCartStreamImage> context, CancellationToken cancellationToken = default)
    {
        var checkoutEvent = JsonSerializer.Deserialize<BasketCheckoutEvent>(context.New!.CheckoutData!)!;

        return Task.FromResult(new PublishInstruction(nameof(BasketCheckoutEvent), checkoutEvent));
    }
}
