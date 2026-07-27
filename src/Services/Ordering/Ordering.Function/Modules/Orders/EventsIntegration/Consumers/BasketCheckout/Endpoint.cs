namespace Ordering.Function;

// EventBridge-triggered consumer: turns a BasketCheckoutEvent into an Order write, idempotent via
// the ordering-processed-events inbox (evt.Id as key) — the inbox write and the order write happen
// in a single TransactWriteItems, so no partial state is possible. DI, per-invocation scoping and
// input deserialization are provided by the Amazon.Lambda.Annotations generator from [LambdaStartup].
public partial class Functions
{
    [LambdaFunction]
    public async Task BasketCheckoutConsumer(
        EventBridgeEvent<BasketCheckoutEvent> evt,
        [FromServices] IIdempotentEventConsumer consumer)
    {
        var command = BasketCheckoutMapper.ToCreateOrderCommand(evt.Detail);
        var order = CreateOrderHandler.CreateNewOrder(command);

        await consumer.ConsumeAsync(evt.Id, [DynamoOrderRepository.ToTransactWriteItem(order)]);
    }
}
