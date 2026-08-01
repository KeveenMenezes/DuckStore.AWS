using Pricing.Function.Shared.Configuration;

namespace Pricing.Function;

// EventBridge-triggered consumer: reacts to PointsRedeemedEvent (CDC from Challenges,
// ADR-0046 §3) and mints a customer-discounts row — no synchronous call to Challenges. Idempotent
// via the pricing-processed-events inbox, same TransactWriteItems shape as
// ProductDeletedConsumer/Ordering's BasketCheckoutConsumer.
//
// Unlike those, the inbox key is the *redemption* id, not evt.Id: challenges-progress-stream-publisher
// re-publishes with a fresh EventBridge id whenever its batch is retried or bisected, so evt.Id would
// let the same redemption mint a second discount. Neither guard downstream would catch it — the
// discount's own attribute_not_exists(DiscountId) protects a Guid minted on this very invocation.
// RedemptionId is the natural key: one redemption, one reward, forever (ADR-0046 §4).
public partial class Functions
{
    [LambdaFunction]
    public async Task PointsRedeemedConsumer(
        EventBridgeEvent<PointsRedeemedEvent> evt,
        [FromServices] RewardOptions rewardOptions,
        [FromServices] IIdempotentEventConsumer consumer)
    {
        var discount = PointsRedeemedMapper.ToCustomerDiscount(evt.Detail, rewardOptions);
        var items = PointsRedeemedHandler.BuildIssueTransactItems(discount);

        await consumer.ConsumeAsync(PointsRedeemedMapper.ToInboxKey(evt.Detail), items);
    }
}
