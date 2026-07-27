namespace Payment.Function;

// EventBridge-triggered consumers: apply the gateway's authorize/decline result to the Payment
// row (Pending -> Authorized/Declined), idempotent via the payment-processed-events inbox. Two
// entry points because EventBridge rules match one detail-type each; both delegate to the same
// ApplyPaymentResultHandler since the two events differ only in outcome, not in processing shape.
public partial class Functions
{
    [LambdaFunction]
    public async Task PaymentAuthorizedConsumer(
        EventBridgeEvent<PaymentAuthorizedEvent> evt,
        [FromServices] IPaymentRepository paymentRepository,
        [FromServices] IIdempotentEventConsumer consumer)
    {
        var command = PaymentResultMapper.ToApplyPaymentResultCommand(evt.Detail);
        var payment = await ApplyPaymentResultHandler.ApplyResultAsync(paymentRepository, command);

        if (payment is not null)
            await consumer.ConsumeAsync(evt.Id, [DynamoPaymentRepository.ToTransactWriteItem(payment)]);
    }

    [LambdaFunction]
    public async Task PaymentDeclinedConsumer(
        EventBridgeEvent<PaymentDeclinedEvent> evt,
        [FromServices] IPaymentRepository paymentRepository,
        [FromServices] IIdempotentEventConsumer consumer)
    {
        var command = PaymentResultMapper.ToApplyPaymentResultCommand(evt.Detail);
        var payment = await ApplyPaymentResultHandler.ApplyResultAsync(paymentRepository, command);

        if (payment is not null)
            await consumer.ConsumeAsync(evt.Id, [DynamoPaymentRepository.ToTransactWriteItem(payment)]);
    }
}
