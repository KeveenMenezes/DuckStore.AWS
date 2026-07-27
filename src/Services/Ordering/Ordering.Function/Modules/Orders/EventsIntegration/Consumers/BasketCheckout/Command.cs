using BuildingBlocks.Core.Validation;

namespace Ordering.Function.Modules.Orders.EventsIntegration.Consumers.BasketCheckout;

public record CreateOrderCommand(
    Guid OrderId,
    Guid CustomerId,
    string OrderName,
    AddressDto ShippingAddress,
    PaymentDto Payment,
    List<CreateOrderItemDto> OrderItems)
    : ICommand<CreateOrderResult>;


public record CreateOrderResult(Guid Id);

public class CreateOrderCommandValidator : IValidator<CreateOrderCommand>
{
    public IEnumerable<ValidationFailure> Validate(CreateOrderCommand instance)
    {
        if (instance.OrderId == Guid.Empty)
        {
            yield return new(nameof(instance.OrderId), "OrderId is required");
        }

        if (instance.CustomerId == Guid.Empty)
        {
            yield return new(nameof(instance.CustomerId), "CustomerId is required");
        }

        if (instance.OrderItems is null || instance.OrderItems.Count == 0)
        {
            yield return new(nameof(instance.OrderItems), "OrderItems should not be empty");
        }

        if (instance.Payment is null)
        {
            yield return new(nameof(instance.Payment), "Payment is required");
            yield break;
        }

        if (!Enum.IsDefined(instance.Payment.PaymentMethod))
        {
            yield return new(nameof(instance.Payment.PaymentMethod), "Invalid payment method");
        }
    }
}
