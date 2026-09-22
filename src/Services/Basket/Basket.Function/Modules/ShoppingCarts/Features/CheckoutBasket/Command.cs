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

        // Basket owns no shipping rules, but Ordering's Address value object rejects a blank
        // AddressLine/EmailAddress — and it only gets to do so after this cart has been deleted
        // and Payment has authorized, so the order silently never exists (the event just lands in
        // ordering-dlq). Checking the same two fields here turns that into a synchronous failure
        // the customer can actually act on, before anything is committed.
        var shippingAddress = instance.BasketCheckoutDto.ShippingAddress;

        if (string.IsNullOrWhiteSpace(shippingAddress?.AddressLine))
        {
            yield return new(nameof(shippingAddress.AddressLine), "AddressLine is required");
        }

        if (string.IsNullOrWhiteSpace(shippingAddress?.EmailAddress))
        {
            yield return new(nameof(shippingAddress.EmailAddress), "EmailAddress is required");
        }
    }
}
