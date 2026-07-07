namespace Payment.Function;

// EventBridge-triggered consumer: turns an OrderCreatedEvent into a Payment write (Status=Pending),
// idempotent via the payment-processed-events inbox (evt.Id as key) — the inbox write and the
// payment write happen in a single TransactWriteItems, so no partial state is possible.
public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task OrderCreatedConsumer(
        EventBridgeEvent<OrderCreatedEvent> evt,
        [FromServices] IIdempotentEventConsumer consumer)
    {
        var command = OrderCreatedMapper.ToCreatePaymentCommand(evt.Detail);
        var payment = CreatePaymentHandler.CreateNewPayment(command);

        await consumer.ConsumeAsync(evt.Id,
        [DynamoPaymentRepository.ToTransactWriteItem(payment)]);
    }
}
