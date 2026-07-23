namespace Payment.Function.Modules.Payments.EventsIntegration.Consumers.BasketCheckout;

public class CreatePaymentHandler(IPaymentRepository paymentRepository)
    : ICommandHandler<CreatePaymentCommand, CreatePaymentResult>
{
    public async Task<CreatePaymentResult> Handle(
        CreatePaymentCommand command, CancellationToken cancellationToken)
    {
        var payment = CreateNewPayment(command);

        await paymentRepository.AddAsync(payment, cancellationToken);

        return new CreatePaymentResult(payment.Id.Value);
    }

    internal static Domain.Entities.Payment CreateNewPayment(CreatePaymentCommand command) =>
        Domain.Entities.Payment.Create(
            id: PaymentId.Of(Guid.NewGuid()),
            orderId: command.OrderId,
            customerId: command.CustomerId,
            amount: command.Amount,
            card: CardDetails.Of(
                command.CardNumber,
                command.Expiration,
                command.Cvv,
                command.PaymentMethod));
}
