namespace Basket.Function.Modules.ShoppingCarts.Features.StoreBasket;

public record StoreBasketCommand(ShoppingCartDto Cart) : ICommand<StoreBasketResult>;
public record StoreBasketResult(string OwnerId);

public class StoreBasketCommandValidator : AbstractValidator<StoreBasketCommand>
{
    public StoreBasketCommandValidator()
    {
        RuleFor(x => x.Cart)
            .NotNull()
            .WithMessage("Cart cannot be null");

        RuleFor(x => x.Cart.OwnerId)
            .NotEmpty()
            .WithMessage("OwnerId cannot be empty or null")
            .When(x => x.Cart is not null);
    }
}
