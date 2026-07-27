using BuildingBlocks.Core.Validation;

namespace Payment.Function.Modules.Payments.EventsIntegration.Consumers.BasketCheckout;

public record CreatePaymentCommand(
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string CardNumber,
    string Expiration,
    string Cvv,
    PaymentMethod PaymentMethod)
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
        if (instance.PaymentMethod != PaymentMethod.Cash && !IsValidCardNumber(instance.CardNumber))
        {
            yield return new(nameof(instance.CardNumber), "Invalid card number");
        }

        if (!Enum.IsDefined(instance.PaymentMethod))
        {
            yield return new(nameof(instance.PaymentMethod), "Invalid payment method");
        }
    }

    // Luhn checksum — the same rule FluentValidation's CreditCard() applied.
    private static bool IsValidCardNumber(string? cardNumber)
    {
        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            return false;
        }

        var sum = 0;
        var even = false;

        for (var i = cardNumber.Length - 1; i >= 0; i--)
        {
            var c = cardNumber[i];
            if (c is ' ' or '-')
            {
                continue;
            }

            if (!char.IsDigit(c))
            {
                return false;
            }

            var digit = c - '0';
            if (even)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            even = !even;
        }

        return sum % 10 == 0;
    }
}
