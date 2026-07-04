namespace Basket.Function.Modules.ShoppingCarts.Features.CheckoutBasket;

public record CheckoutBasketCommand(BasketCheckoutDto BasketCheckoutDto)
    : ICommand<CheckoutBasketResult>;

public record CheckoutBasketResult(bool IsSuccess);

public class CheckoutBasketCommandValidator
    : AbstractValidator<CheckoutBasketCommand>
{
    public CheckoutBasketCommandValidator()
    {
        RuleFor(x => x.BasketCheckoutDto)
            .NotNull()
            .WithMessage("BasketCheckoutDto can't be null");

        RuleFor(x => x.BasketCheckoutDto.OwnerId)
            .NotEmpty()
            .WithMessage("OwnerId is required")
            .When(x => x.BasketCheckoutDto != null);
    }
}
