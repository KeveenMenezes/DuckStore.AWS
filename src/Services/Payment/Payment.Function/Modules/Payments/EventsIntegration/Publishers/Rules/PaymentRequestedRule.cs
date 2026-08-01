namespace Payment.Function.Modules.Payments.EventsIntegration.Publishers.Rules;

// Publishes PaymentRequestedEvent when a new, still-pending payment row lands in the table. The
// aggregate is rehydrated from the repository (a domain abstraction — not AWS infrastructure);
// the rule never references EventBridge and only returns a generic PublishInstruction.
public sealed class PaymentRequestedRule(IPaymentRepository payments) : IStreamRule<PaymentStreamImage>
{
    public bool Match(StreamContext<PaymentStreamImage> context) =>
        context.EventName == "INSERT" &&
        context.New?.Type == "Payment" &&
        context.New?.Status == "Pending";

    public async Task<PublishInstruction> BuildAsync(
        StreamContext<PaymentStreamImage> context, CancellationToken cancellationToken = default)
    {
        var payment = await payments.GetByIdAsync(context.New!.Id, cancellationToken);

        return new PublishInstruction(nameof(PaymentRequestedEvent), ToPaymentRequestedEvent(payment!));
    }

    private static PaymentRequestedEvent ToPaymentRequestedEvent(Domain.Entities.Payment payment) => new()
    {
        PaymentId = payment.Id.Value,
        OrderId = payment.OrderId,
        CustomerId = payment.CustomerId,
        Amount = payment.Amount,
        CardNumber = payment.Card.CardNumber,
        Expiration = payment.Card.Expiration,
        Cvv = payment.Card.Cvv,
        PaymentMethod = (int)payment.Card.PaymentMethod,
        DiscountId = payment.DiscountId
    };
}
