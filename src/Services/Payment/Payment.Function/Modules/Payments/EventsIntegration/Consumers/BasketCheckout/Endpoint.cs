namespace Payment.Function;

// EventBridge-triggered consumer: turns a BasketCheckoutEvent into a Payment write (Status=Pending),
// idempotent via the payment-processed-events inbox (evt.Id as key) — the inbox write and the
// payment write happen in a single TransactWriteItems, so no partial state is possible.
// Consumes BasketCheckoutEvent directly (not OrderCreatedEvent) so card data never has to be
// persisted or republished by Ordering (see ADR-0038) — Ordering and Payment are independent,
// parallel consumers of the same event.
public partial class Functions
{
    [LambdaFunction]
    public async Task BasketCheckoutConsumer(
        EventBridgeEvent<BasketCheckoutEvent> evt,
        [FromServices] IIdempotentEventConsumer consumer)
    {
        var command = BasketCheckoutMapper.ToCreatePaymentCommand(evt.Detail);
        var payment = CreatePaymentHandler.CreateNewPayment(command);

        await consumer.ConsumeAsync(evt.Id,
        [DynamoPaymentRepository.ToTransactWriteItem(payment)]);
    }
}
