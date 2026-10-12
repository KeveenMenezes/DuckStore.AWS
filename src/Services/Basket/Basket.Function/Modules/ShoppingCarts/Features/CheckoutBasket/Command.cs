using BuildingBlocks.Core.Validation;

namespace Basket.Function.Modules.ShoppingCarts.Features.CheckoutBasket;

// CheckoutId/CartVersion come from the checkout saga (ADR-0050 §4); without a CheckoutId this is the
// old pipeline's checkout, accepted only while the saga rolls out (ADR-0050 §3).
public record CheckoutBasketCommand(
    BasketCheckoutDto BasketCheckoutDto, string? CheckoutId = null, string? CartVersion = null)
    : ICommand<CheckoutBasketResult>;

// OrderId is the id the checkout payload carries and Ordering keys the order off of (ADR-0038),
// returned so the customer sees the real order number. Null when there was no cart to check out.
// ErrorType is a typed refusal (CheckoutRefusals) the saga may compensate, returned as data.
public record CheckoutBasketResult(bool IsSuccess, Guid? OrderId = null, string? ErrorType = null);

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

        // The saga derives the OrderId and builds the priced lines before calling Basket (ADR-0050 §3/§5).
        if (instance.CheckoutId is not null)
        {
            if (instance.BasketCheckoutDto.OrderId == Guid.Empty)
            {
                yield return new(nameof(instance.BasketCheckoutDto.OrderId), "OrderId is required");
            }

            if (instance.BasketCheckoutDto.Items is not { Count: > 0 })
            {
                yield return new(nameof(instance.BasketCheckoutDto.Items), "Items is required");
            }
        }
    }
}
