namespace PaymentGateway.Function;

// EventBridge-triggered consumer: runs the simulated gateway decision and publishes the result
// directly. No idempotency inbox — the decision is a pure function of (CardNumber, Amount), so
// reprocessing a duplicate delivery is harmless; Payment.Function's own PaymentResult consumer
// is the one with the idempotency guard against double-applying the result (ADR-0025).
public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task PaymentRequestedConsumer(
        EventBridgeEvent<PaymentRequestedEvent> evt,
        [FromServices] IEventPublisher publisher)
    {
        var (authorized, detail) = SimulatedGatewayDecision.Decide(evt.Detail.CardNumber, evt.Detail.Amount);

        if (authorized)
        {
            await publisher.PublishAsync(new PaymentAuthorizedEvent
            {
                PaymentId = evt.Detail.PaymentId,
                OrderId = evt.Detail.OrderId,
                AuthorizationCode = detail
            });
        }
        else
        {
            await publisher.PublishAsync(new PaymentDeclinedEvent
            {
                PaymentId = evt.Detail.PaymentId,
                OrderId = evt.Detail.OrderId,
                DeclineReason = detail
            });
        }
    }
}
