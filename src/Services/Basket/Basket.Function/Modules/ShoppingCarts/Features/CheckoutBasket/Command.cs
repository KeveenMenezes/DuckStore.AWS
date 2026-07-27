using BuildingBlocks.Core.Validation;

namespace Basket.Function.Modules.ShoppingCarts.Features.CheckoutBasket;

public record CheckoutBasketCommand(BasketCheckoutDto BasketCheckoutDto)
    : ICommand<CheckoutBasketResult>;

public record CheckoutBasketResult(bool IsSuccess);

public class CheckoutBasketCommandValidator : IValidator<CheckoutBasketCommand>
{
    public IEnumerable<ValidationFailure> Validate(CheckoutBasketCommand instance)
    {
        if (instance.BasketCheckoutDto is null)
        {
            yield return new(nameof(instance.BasketCheckoutDto), "BasketCheckoutDto can't be null");
            yield break;
        }

        if (string.IsNullOrEmpty(instance.BasketCheckoutDto.OwnerId))
        {
            yield return new(nameof(instance.BasketCheckoutDto.OwnerId), "OwnerId is required");
        }
    }
}
