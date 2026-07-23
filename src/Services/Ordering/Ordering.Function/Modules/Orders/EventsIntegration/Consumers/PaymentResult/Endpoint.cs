namespace Ordering.Function;

// EventBridge-triggered consumers: apply Payment's authorize/decline result to the Order
// (Pending -> Completed/Cancelled), idempotent via the ordering-processed-events inbox. Two
// entry points because EventBridge rules match one detail-type each; both delegate to the same
// ApplyPaymentResultHandler since the two events differ only in outcome, not in processing shape.
public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task OrderPaymentAuthorizedConsumer(
        EventBridgeEvent<PaymentAuthorizedEvent> evt,
        [FromServices] IOrderRepository orderRepository,
        [FromServices] IIdempotentEventConsumer consumer)
    {
        var command = PaymentResultMapper.ToApplyPaymentResultCommand(evt.Detail);
        var order = await ApplyPaymentResultHandler.ApplyResultAsync(orderRepository, command);

        if (order is not null)
            await consumer.ConsumeAsync(evt.Id, [DynamoOrderRepository.ToTransactWriteItem(order)]);
    }

    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task OrderPaymentDeclinedConsumer(
        EventBridgeEvent<PaymentDeclinedEvent> evt,
        [FromServices] IOrderRepository orderRepository,
        [FromServices] IIdempotentEventConsumer consumer)
    {
        var command = PaymentResultMapper.ToApplyPaymentResultCommand(evt.Detail);
        var order = await ApplyPaymentResultHandler.ApplyResultAsync(orderRepository, command);

        if (order is not null)
            await consumer.ConsumeAsync(evt.Id, [DynamoOrderRepository.ToTransactWriteItem(order)]);
    }
}
