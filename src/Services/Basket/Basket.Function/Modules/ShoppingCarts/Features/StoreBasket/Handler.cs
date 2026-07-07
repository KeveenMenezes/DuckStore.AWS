namespace Basket.Function.Modules.ShoppingCarts.Features.StoreBasket;

public class StoreBasketCommandHandler(IShoppingCartRepository repository)
    : ICommandHandler<StoreBasketCommand, StoreBasketResult>
{
    public async Task<StoreBasketResult> Handle(StoreBasketCommand command, CancellationToken cancellationToken)
    {
        var cart = ToAggregate(command.Cart);

        await repository.StoreCart(cart, cancellationToken);

        return new StoreBasketResult(cart.OwnerId);
    }

    private static ShoppingCart ToAggregate(ShoppingCartDto dto) =>
        ShoppingCart.Create(
            dto.OwnerId,
            dto.Items.Select(item =>
                ShoppingCartItem.Create(
                    ProductId.Of(item.ProductId), item.ProductName, item.ImageUrl, item.Color, item.Quantity, item.Price)));
}
