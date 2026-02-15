namespace Ordering.Application.Orders.Commands.CreateOrder;

public record CreateOrderCommand(
    Guid CustomerId,
    string OrderName,
    AddressDto ShippingAddress,
    PaymentDto Payment,
    List<CreateOrderItemDto> OrderItems)
    : ICommand<CreateOrderResult>;


public record CreateOrderResult(Guid Id);

public class CreateOrderCommandValidator
    : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty()
            .WithMessage("CustomerId is required");

        RuleFor(x => x.OrderItems)
            .NotEmpty()
            .WithMessage("OrderItems should not be empty");

        RuleFor(x => x.Payment)
            .NotNull()
            .WithMessage("Payment is required");

        RuleFor(x => x.Payment.CardNumber)
            .CreditCard()
            .WithMessage("Invalid card number");

        RuleFor(x => x.Payment.PaymentMethod)
            .IsInEnum()
            .WithMessage("Invalid payment method");
    }
}
