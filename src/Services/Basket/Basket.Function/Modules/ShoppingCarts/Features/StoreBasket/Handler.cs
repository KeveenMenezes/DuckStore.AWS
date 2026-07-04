namespace Basket.Function.Modules.ShoppingCarts.Features.StoreBasket;

public class StoreBasketCommandHandler(
    IShoppingCartRepository repository,
    ICouponRepository couponRepository)
    : ICommandHandler<StoreBasketCommand, StoreBasketResult>
{
    public async Task<StoreBasketResult> Handle(StoreBasketCommand command, CancellationToken cancellationToken)
    {
        var cart = ToAggregate(command.Cart);

        var coupons = await GetCouponsAsync(cart, cancellationToken);
        cart.ApplyDiscounts(coupons);

        await repository.StoreCart(cart, cancellationToken);

        return new StoreBasketResult(cart.OwnerId);
    }

    private static ShoppingCart ToAggregate(ShoppingCartDto dto) =>
        ShoppingCart.Create(
            dto.OwnerId,
            dto.Items.Select(item =>
                ShoppingCartItem.Create(
                    ProductId.Of(item.ProductId), item.ProductName, item.ImageUrl, item.Color, item.Quantity, item.Price)));

    private async Task<IReadOnlyCollection<Coupon>> GetCouponsAsync(
        ShoppingCart cart, CancellationToken cancellationToken)
    {
        var coupons = new List<Coupon>(cart.Items.Count);

        foreach (var item in cart.Items)
        {
            coupons.Add(
                await couponRepository.GetByProductNameAsync(item.ProductName, cancellationToken)
                ?? Coupon.NoDiscountFor(item.ProductName));
        }

        return coupons;
    }
}
