namespace Payment.Function.Modules.Payments.EventsIntegration.Consumers.OrderCreated;

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

public class CreatePaymentCommandValidator : AbstractValidator<CreatePaymentCommand>
{
    public CreatePaymentCommandValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty()
            .WithMessage("OrderId is required");

        RuleFor(x => x.CustomerId)
            .NotEmpty()
            .WithMessage("CustomerId is required");

        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("Amount must be greater than zero");

        RuleFor(x => x.CardNumber)
            .CreditCard()
            .WithMessage("Invalid card number")
            .When(x => x.PaymentMethod != PaymentMethod.Cash);

        RuleFor(x => x.PaymentMethod)
            .IsInEnum()
            .WithMessage("Invalid payment method");
    }
}
