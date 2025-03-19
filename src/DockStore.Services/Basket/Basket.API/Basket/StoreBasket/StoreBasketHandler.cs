namespace Basket.API.Basket.StoreBasket;

public record StoreBasketCommand(ShoppingCart Cart) : ICommand<StoreBasketResult>;
public record StoreBasketResult(string UserName);

public class StoreBasketCommandValidator : AbstractValidator<StoreBasketCommand>
{
    public StoreBasketCommandValidator()
    {
        RuleFor(x => x.Cart)
            .NotNull()
            .WithMessage("Cart cannot be null");

        RuleFor(x => x.Cart.UserName)
            .NotNull()
            .NotEmpty()
            .WithMessage("UserName cannot be empty or null");
    }
}

public class StoreBasketCommandHandler(
    IBasketRepository repository,
    DiscountProtoService.DiscountProtoServiceClient discountProtoServiceClient)
    : ICommandHandler<StoreBasketCommand, StoreBasketResult>
{
    public async Task<StoreBasketResult> Handle(StoreBasketCommand command, CancellationToken cancellationToken)
    {
        await DeductDiscount(command, cancellationToken);

        await repository.StoreCart(command.Cart, cancellationToken);

        return new StoreBasketResult(command.Cart.UserName);
    }

    private async Task DeductDiscount(StoreBasketCommand command, CancellationToken cancellationToken)
    {
        foreach (var item in command.Cart.Items)
        {
            var coupon = await discountProtoServiceClient.GetDiscountAsync(
                new GetDiscountRequest { ProductName = item.ProductName },
                cancellationToken: cancellationToken);
            item.Price -= coupon.Amount;
        }
    }
}
