namespace Payment.Function.Modules.Payments.EventsIntegration.Consumers.BasketCheckout;

public class CreatePaymentHandler(IPaymentRepository paymentRepository)
    : ICommandHandler<CreatePaymentCommand, CreatePaymentResult>
{
    public async ValueTask<CreatePaymentResult> Handle(
        CreatePaymentCommand command, CancellationToken cancellationToken)
    {
        var payment = CreateNewPayment(command);

        await paymentRepository.AddAsync(payment, cancellationToken);

        return new CreatePaymentResult(payment.Id.Value);
    }

    // The payment writes the checkout event commits with its inbox record. The checkout saga
    // registered the payment keyed by OrderId (ADR-0050 §6): promote it, conditioned on it still
    // being Registered. No registered payment means the event came from the old checkout pipeline,
    // which still needs the payment created here until the saga is live (ADR-0050 §3). Any other
    // status (already promoted, voided) writes nothing — only the inbox.
    public static async Task<IReadOnlyList<TransactWriteItem>> ToPaymentWritesAsync(
        IPaymentRepository paymentRepository,
        CreatePaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        var registered = await paymentRepository.GetByIdAsync(PaymentId.Of(command.OrderId).Value, cancellationToken);

        if (registered is null)
            return [DynamoPaymentRepository.ToTransactWriteItem(CreateNewPayment(command))];

        // Conditioned on the status it was read in, so a concurrent change cancels the transaction
        // and EventBridge retries against the new state.
        var readStatus = registered.Status;
        return registered.Promote(awaitingPoints: command.PointsToUse > 0)
            ? [DynamoPaymentRepository.ToTransactWriteItem(registered, expectedStatus: readStatus)]
            : [];
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
                command.PaymentMethod),
            awaitingPoints: command.PointsToUse > 0);
}
