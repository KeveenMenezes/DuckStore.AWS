namespace Payment.Function.Modules.Payments.EventsIntegration.Publishers.Rules;

// Publishes PaymentRequestedEvent when a payment becomes Pending: on the MODIFY Registered → Pending
// when the checkout event promotes a payment the checkout saga registered (ADR-0050 §6) — or
// Voided → Pending when a void raced a successful close of the same attempt — on the
// MODIFY AwaitingPoints → Pending once Challenges reserved the checkout's points (ADR-0048 §6).
// Registered and Voided never match, so they never reach the gateway.
// The aggregate is rehydrated from the repository (a domain abstraction — not AWS infrastructure);
// the rule never references EventBridge and only returns a generic PublishInstruction.
public sealed class PaymentRequestedRule(IPaymentRepository payments) : IStreamRule<PaymentStreamImage>
{
    private static readonly string Pending = nameof(PaymentStatus.Pending);
    private static readonly string AwaitingPoints = nameof(PaymentStatus.AwaitingPoints);
    private static readonly string Registered = nameof(PaymentStatus.Registered);
    private static readonly string Voided = nameof(PaymentStatus.Voided);

    public bool Match(StreamContext<PaymentStreamImage> context) =>
        context.New is { Type: "Payment" } next
        && next.Status == Pending
        && context.EventName == "MODIFY"
        && (context.Old?.Status == AwaitingPoints || context.Old?.Status == Registered || context.Old?.Status == Voided);

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
        PaymentMethod = (int)payment.Card.PaymentMethod
    };
}
