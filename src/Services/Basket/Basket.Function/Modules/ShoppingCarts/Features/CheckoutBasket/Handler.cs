namespace Basket.Function.Modules.ShoppingCarts.Features.CheckoutBasket;

public class CheckoutBasketCommandHandler(IShoppingCartRepository basketRepository)
    : ICommandHandler<CheckoutBasketCommand, CheckoutBasketResult>
{
    public async Task<CheckoutBasketResult> Handle(
        CheckoutBasketCommand command, CancellationToken cancellationToken)
    {
        var basket = await basketRepository.TryGetBasket(
            command.BasketCheckoutDto.OwnerId, cancellationToken);

        if (basket == null)
        {
            return new CheckoutBasketResult(false);
        }

        // Generated here — the correlation id Ordering and Payment both key their own aggregate
        // off of, so neither has to mint its own (ADR-0038).
        command.BasketCheckoutDto.OrderId = Guid.NewGuid();

        // Write checkout payload to DynamoDB before deletion so DynamoDB Streams captures it
        // and the ShoppingCartStreamPublisher Lambda's CheckoutedRule can publish to EventBridge.
        var checkoutDataJson = JsonSerializer.Serialize(command.BasketCheckoutDto);
        await basketRepository.MarkCheckoutAsync(
            command.BasketCheckoutDto.OwnerId, checkoutDataJson, cancellationToken);

        await basketRepository.DeleteBasket(
            command.BasketCheckoutDto.OwnerId, cancellationToken);

        return new CheckoutBasketResult(true);
    }
}
