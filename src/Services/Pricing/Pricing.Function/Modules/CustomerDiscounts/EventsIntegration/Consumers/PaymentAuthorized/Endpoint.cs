using Pricing.Function.Modules.CustomerDiscounts.EventsIntegration.Consumers.PaymentAuthorized;

namespace Pricing.Function;

// EventBridge-triggered consumer: reacts to PaymentAuthorizedEvent (published by
// PaymentGateway.Function — the same event Ordering's own consumer reacts to, ADR-0025) and burns
// the customer discount used at checkout, if any (ADR-0046 §6). Burning at authorization, not
// checkout, is deliberate: a declined payment must leave the discount Issued and reusable — nothing
// here reacts to PaymentDeclinedEvent. Idempotent via pricing-processed-events; the shared
// idempotent consumer's ConditionalCheckFailed catch-all also covers the double-spend case (a
// second authorization trying to burn an already-Consumed discount) as the same silent no-op
// (ADR-0046 §6, mitigation).
public partial class Functions
{
    [LambdaFunction]
    public async Task PaymentAuthorizedConsumer(
        EventBridgeEvent<PaymentAuthorizedEvent> evt,
        [FromServices] IIdempotentEventConsumer consumer)
    {
        if (string.IsNullOrWhiteSpace(evt.Detail.DiscountId))
        {
            return;
        }

        var ownerId = PaymentAuthorizedMapper.ToOwnerId(evt.Detail.CustomerId);
        var items = PaymentAuthorizedHandler.BuildConsumeTransactItems(ownerId, evt.Detail.DiscountId);

        await consumer.ConsumeAsync(evt.Id, items);
    }
}
