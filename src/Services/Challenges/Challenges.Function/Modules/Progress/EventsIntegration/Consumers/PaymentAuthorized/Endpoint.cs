using Challenges.Function.Modules.Progress.EventsIntegration.Consumers.PaymentAuthorized;

namespace Challenges.Function;

public partial class Functions
{
    // EventBridge-triggered consumer (challenges-payment-authorized-consumer): marks the order's
    // reserved points Used once the payment is authorized (ADR-0048 §5). Every authorized payment
    // reaches it; an order without points has no REDEMPTION# row and is a no-op. The conditional
    // status write makes a redelivery a no-op too, so there is no processed-events inbox.
    [LambdaFunction]
    public async Task PaymentAuthorizedConsumer(
        EventBridgeEvent<PaymentAuthorizedEvent> evt,
        [FromServices] PaymentAuthorizedHandler handler)
    {
        await handler.HandleAsync(PaymentAuthorizedMapper.ToCommand(evt.Detail));
    }
}
