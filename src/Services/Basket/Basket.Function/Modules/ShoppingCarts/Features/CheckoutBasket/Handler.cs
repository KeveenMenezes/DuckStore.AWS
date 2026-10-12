using Basket.Function.Shared.Configuration;
namespace Basket.Function.Modules.ShoppingCarts.Features.CheckoutBasket;

public class CheckoutBasketCommandHandler(IShoppingCartRepository basketRepository)
    : ICommandHandler<CheckoutBasketCommand, CheckoutBasketResult>
{
    public async ValueTask<CheckoutBasketResult> Handle(
        CheckoutBasketCommand command, CancellationToken cancellationToken)
    {
        return await CloseForSagaAsync(command, cancellationToken);
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
