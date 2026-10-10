using Challenges.Function.Modules.Progress.EventsIntegration.Consumers.BasketCheckout;

namespace Challenges.Function;

public partial class Functions
{
    // EventBridge-triggered consumer (challenges-basket-checkout-consumer): reserves the points a
    // checkout used (ADR-0048 §5) — debit + REDEMPTION#<orderId> Reserved, or a Failed row when the
    // balance is short. Checkouts without points are ignored. The deterministic ledger id makes a
    // redelivered event a no-op, so there is no processed-events inbox.
    [LambdaFunction]
    public async Task BasketCheckoutConsumer(
        EventBridgeEvent<BasketCheckoutEvent> evt,
        [FromServices] BasketCheckoutHandler handler)
    {
        var command = BasketCheckoutMapper.ToCommand(evt.Detail);
        if (command is null)
        {
            return;
        }

        await handler.HandleAsync(command);
    }
}
