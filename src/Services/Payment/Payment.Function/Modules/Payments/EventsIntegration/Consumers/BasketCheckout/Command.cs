using BuildingBlocks.Core.Validation;
using Payment.Function.Modules.Payments.Domain.Services;

namespace Payment.Function.Modules.Payments.EventsIntegration.Consumers.BasketCheckout;

public record CreatePaymentCommand(
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string CardNumber,
    string Expiration,
    string Cvv,
    PaymentMethod PaymentMethod,
    int PointsToUse = 0)
    : ICommand<CreatePaymentResult>;

public record CreatePaymentResult(Guid Id);

public class CreatePaymentCommandValidator : IValidator<CreatePaymentCommand>
{
    public IEnumerable<ValidationFailure> Validate(CreatePaymentCommand instance)
    {
        if (instance.OrderId == Guid.Empty)
        {
            yield return new(nameof(instance.OrderId), "OrderId is required");
        }

        if (instance.CustomerId == Guid.Empty)
        {
            yield return new(nameof(instance.CustomerId), "CustomerId is required");
        }

        if (instance.Amount <= 0)
        {
            yield return new(nameof(instance.Amount), "Amount must be greater than zero");
        }

        // Cash orders carry no card, so the Luhn check only applies to the other methods.
        if (instance.PaymentMethod != PaymentMethod.Cash && !Luhn.IsValid(instance.CardNumber))
        {
            yield return new(nameof(instance.CardNumber), "Invalid card number");
        }

        if (!Enum.IsDefined(instance.PaymentMethod))
        {
            yield return new(nameof(instance.PaymentMethod), "Invalid payment method");
        }
    }
}
