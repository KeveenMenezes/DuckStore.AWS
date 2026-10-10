using Review.Function.Modules.Reviews.EventsIntegration.Consumers.OrderCompleted;

namespace Review.Function;

public partial class Functions
{
    // EventBridge-triggered consumer (review-order-completed-consumer): when Ordering publishes
    // OrderCompletedEvent, creates one Eligible review row per purchased product so only buyers can
    // review (ADR-0049 §3). Each product is an individual PutItem conditioned on
    // attribute_not_exists(Id); an existing row is a silent no-op, which also makes redelivery
    // idempotent — hence no processed-events inbox and no TransactWriteItems.
    [LambdaFunction]
    public async Task OrderCompletedConsumer(
        EventBridgeEvent<OrderCompletedEvent> evt,
        [FromServices] OrderCompletedHandler handler)
    {
        var command = OrderCompletedMapper.ToCommand(evt.Detail);

        await handler.HandleAsync(command);
    }
}
