namespace Ordering.Application.Orders.Commands.CreateOrder;

public record CreateOrderCommand(OrderDto Order)
    : ICommand<CreateOrderResult>;

public record CreateOrderResult(Guid Id);

public class CreateOrderCommandValidator
    : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.Order.OrderName)
            .NotEmpty()
            .WithMessage("Name is required");

        RuleFor(x => x.Order.CustomerId)
            .NotEmpty()
            .WithMessage("CustomerId is required");

        RuleFor(x => x.Order.OrderItems)
            .NotEmpty()
            .WithMessage("OrderItems should not be empty");

        RuleFor(x => x.Order.Payment)
            .NotNull()
            .WithMessage("Payment is required");

        When(x => x.Order.Payment != null, () =>
        {
            RuleFor(x => x.Order.Payment.CardNumber)
                .CreditCard()
                .WithMessage("Invalid card number");

            RuleFor(x => x.Order.Payment.PaymentMethod)
                .IsInEnum()
                .WithMessage("Invalid payment method");
        });

        RuleFor(x => x.Order.Status)
            .IsInEnum()
            .WithMessage("Invalid order status");
    }
}