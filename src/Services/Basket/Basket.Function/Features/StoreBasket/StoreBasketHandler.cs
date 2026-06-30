namespace Basket.Function.Features.StoreBasket;

public record StoreBasketCommand(ShoppingCartDto Cart) : ICommand<StoreBasketResult>;
public record StoreBasketResult(string UserName);

public class StoreBasketCommandValidator : AbstractValidator<StoreBasketCommand>
{
    public StoreBasketCommandValidator()
    {
        RuleFor(x => x.Cart)
            .NotNull()
            .WithMessage("Cart cannot be null");

        RuleFor(x => x.Cart.UserName)
            .NotEmpty()
            .WithMessage("UserName cannot be empty or null")
            .When(x => x.Cart is not null);
    }
}

public class StoreBasketCommandHandler(
    IBasketRepository repository,
    ICouponRepository couponRepository)
    : ICommandHandler<StoreBasketCommand, StoreBasketResult>
{
    public async Task<StoreBasketResult> Handle(StoreBasketCommand command, CancellationToken cancellationToken)
    {
        var cart = ToAggregate(command.Cart);

        var coupons = await GetCouponsAsync(cart, cancellationToken);
        cart.ApplyDiscounts(coupons);

        await repository.StoreCart(cart, cancellationToken);

        return new StoreBasketResult(cart.UserName);
    }

    private static ShoppingCart ToAggregate(ShoppingCartDto dto) =>
        ShoppingCart.Create(
            dto.UserName,
            dto.Items.Select(item =>
                ShoppingCartItem.Create(item.ProductId, item.ProductName, item.Color, item.Quantity, item.Price)));

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
