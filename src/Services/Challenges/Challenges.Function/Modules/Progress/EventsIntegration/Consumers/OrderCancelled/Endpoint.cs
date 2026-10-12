using Challenges.Function.Modules.Progress.EventsIntegration.Consumers.OrderCancelled;

namespace Challenges.Function;

public partial class Functions
{
    // EventBridge-triggered consumer (challenges-order-cancelled-consumer): gives back the points of
    // a cancelled order — Released before payment, Refunded after (ADR-0048 §5). One consumer covers
    // a declined payment and a cancellation after payment, since a decline already cancels the order.
    // Orders without points have no REDEMPTION# row and are a no-op; the conditional status write
    // makes a redelivery a no-op too.
    [LambdaFunction]
    public async Task OrderCancelledConsumer(
        EventBridgeEvent<OrderCancelledEvent> evt,
        [FromServices] OrderCancelledHandler handler)
    {
        await handler.HandleAsync(OrderCancelledMapper.ToCommand(evt.Detail));
    }
}
