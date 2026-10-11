using Challenges.Function.Modules.Progress.EventsIntegration.Consumers.ReviewCreated;

namespace Challenges.Function;

public partial class Functions
{
    // EventBridge-triggered consumer (challenges-review-created-consumer), the first inbound
    // consumer of Challenges (ADR-0048 amends ADR-0045): when Review publishes ReviewCreatedEvent
    // (Eligible|Deleted -> Published, ADR-0049), credits PointsPolicy.ReviewCredit once per
    // product. The deterministic REVIEW#<productId> ledger row is the idempotency key, so there is
    // no processed-events inbox — a duplicate cancels the transaction and is a silent no-op.
    [LambdaFunction]
    public async Task ReviewCreatedConsumer(
        EventBridgeEvent<ReviewCreatedEvent> evt,
        [FromServices] ReviewCreatedHandler handler)
    {
        var command = ReviewCreatedMapper.ToCommand(evt.Detail);

        await handler.HandleAsync(command);
    }
}
