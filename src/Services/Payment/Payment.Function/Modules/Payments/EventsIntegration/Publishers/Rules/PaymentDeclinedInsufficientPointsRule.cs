namespace Payment.Function.Modules.Payments.EventsIntegration.Publishers.Rules;

// Publishes PaymentDeclinedEvent when a payment's points couldn't be reserved (MODIFY
// AwaitingPoints → Declined, ADR-0048 §6). Every other decline comes from PaymentGateway, which
// publishes the event itself; this transition never reaches the gateway, so Payment announces it
// with the same, unchanged contract and Ordering cancels the order through its existing consumer.
// Payment's own PaymentDeclinedConsumer also receives it and is a no-op (the payment is no longer
// Pending).
public sealed class PaymentDeclinedInsufficientPointsRule(IPaymentRepository payments)
    : IStreamRule<PaymentStreamImage>
{
    private static readonly string AwaitingPoints = nameof(PaymentStatus.AwaitingPoints);
    private static readonly string Declined = nameof(PaymentStatus.Declined);

    public bool Match(StreamContext<PaymentStreamImage> context) =>
        context.EventName == "MODIFY"
        && context.New is { Type: "Payment" } next
        && next.Status == Declined
        && context.Old?.Status == AwaitingPoints;

    public async Task<PublishInstruction> BuildAsync(
        StreamContext<PaymentStreamImage> context, CancellationToken cancellationToken = default)
    {
        var payment = await payments.GetByIdAsync(context.New!.Id, cancellationToken);

        return new PublishInstruction(nameof(PaymentDeclinedEvent), new PaymentDeclinedEvent
        {
            PaymentId = payment!.Id.Value,
            OrderId = payment.OrderId,
            DeclineReason = payment.DeclineReason ?? Domain.Entities.Payment.InsufficientPointsReason
        });
    }
}
