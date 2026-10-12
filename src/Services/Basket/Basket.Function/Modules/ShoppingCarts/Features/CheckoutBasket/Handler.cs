using Basket.Function.Shared.Configuration;
namespace Basket.Function.Modules.ShoppingCarts.Features.CheckoutBasket;

public class CheckoutBasketCommandHandler(IShoppingCartRepository basketRepository)
    : ICommandHandler<CheckoutBasketCommand, CheckoutBasketResult>
{
    public async ValueTask<CheckoutBasketResult> Handle(
        CheckoutBasketCommand command, CancellationToken cancellationToken)
    {
        if (command.CheckoutId is not null)
        {
            return await CloseForSagaAsync(command, cancellationToken);
        }

        var basket = await basketRepository.TryGetBasket(
            command.BasketCheckoutDto.OwnerId, cancellationToken);

        if (basket == null)
        {
            return new CheckoutBasketResult(false);
        }

        // Generated here — the correlation id Ordering and Payment both key their own aggregate
        // off of, so neither has to mint its own (ADR-0038).
        command.BasketCheckoutDto.OrderId = Guid.NewGuid();

        // The real cart lines being checked out — Ordering.Function builds the actual OrderItems
        // from this instead of hardcoded placeholders.
        command.BasketCheckoutDto.Items = basket.Items
            .Select(item => new BasketCheckoutItemDto(
                item.ProductId.Value, item.ProductName, item.ImageId, item.Quantity, item.Price))
            .ToList();

        // Write checkout payload to DynamoDB before deletion so DynamoDB Streams captures it
        // and the ShoppingCartStreamPublisher Lambda's CheckoutedRule can publish to EventBridge.
        var checkoutDataJson = JsonSerializer.Serialize(
            command.BasketCheckoutDto, BasketSerializerContext.Default.BasketCheckoutDto);
        await basketRepository.MarkCheckoutAsync(
            command.BasketCheckoutDto.OwnerId, checkoutDataJson, cancellationToken);

        await basketRepository.DeleteBasket(
            command.BasketCheckoutDto.OwnerId, cancellationToken);

        return new CheckoutBasketResult(true, command.BasketCheckoutDto.OrderId);
    }

    // The saga already derived the OrderId and priced and named the lines (ADR-0050 §3/§5), so the
    // cart is closed as given, never re-read: whatever changed since the quote is caught by the
    // Version condition instead.
    private async Task<CheckoutBasketResult> CloseForSagaAsync(
        CheckoutBasketCommand command, CancellationToken cancellationToken)
    {
        var dto = command.BasketCheckoutDto;
        var checkoutDataJson = JsonSerializer.Serialize(dto, BasketSerializerContext.Default.BasketCheckoutDto);

        var closed = await basketRepository.CloseCartAsync(
            dto.OwnerId, command.CheckoutId!, command.CartVersion, dto.OrderId, checkoutDataJson, cancellationToken);

        return closed.Outcome == CartCloseOutcome.CartChanged
            ? new CheckoutBasketResult(false, ErrorType: CheckoutRefusals.CartChanged)
            : new CheckoutBasketResult(true, closed.OrderId);
    }
}
