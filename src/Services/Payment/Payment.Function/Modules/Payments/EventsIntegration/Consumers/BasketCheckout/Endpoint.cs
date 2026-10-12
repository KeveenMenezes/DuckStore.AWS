namespace Payment.Function;

// EventBridge-triggered consumer: promotes the payment the checkout saga registered for the order
// (Registered → Pending, or → AwaitingPoints when the checkout used points — ADR-0050 §6), or, for an
// event from the old checkout pipeline, creates the payment as before. Idempotent via the
// payment-processed-events inbox (evt.Id as key): the inbox write and the payment write happen in a
// single TransactWriteItems, so no partial state is possible.
// Consumes BasketCheckoutEvent directly (not OrderCreatedEvent) — Ordering and Payment are
// independent, parallel consumers of the same event (ADR-0038).
public partial class Functions
{
    [LambdaFunction]
    public async Task BasketCheckoutConsumer(
        EventBridgeEvent<BasketCheckoutEvent> evt,
        [FromServices] IPaymentRepository paymentRepository,
        [FromServices] IIdempotentEventConsumer consumer)
    {
        var command = BasketCheckoutMapper.ToCreatePaymentCommand(evt.Detail);
        var writes = await CreatePaymentHandler.ToPaymentWritesAsync(paymentRepository, command);

        await consumer.ConsumeAsync(evt.Id, writes);
    }
}
