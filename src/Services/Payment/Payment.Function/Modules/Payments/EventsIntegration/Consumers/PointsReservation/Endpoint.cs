using Payment.Function.Modules.Payments.EventsIntegration.Consumers.PointsReservation;

namespace Payment.Function;

// EventBridge-triggered consumers: apply Challenges' points reservation outcome to the order's
// payment (AwaitingPoints -> Pending, or -> Declined with InsufficientPoints — ADR-0048 §6),
// idempotent via the payment-processed-events inbox. Two entry points because EventBridge rules
// match one detail-type each, the same shape as PaymentResult; both delegate to
// PointsReservationHandler.
public partial class Functions
{
    [LambdaFunction]
    public async Task PointsReservedConsumer(
        EventBridgeEvent<PointsReservedEvent> evt,
        [FromServices] IPaymentRepository paymentRepository,
        [FromServices] IIdempotentEventConsumer consumer)
    {
        var payment = await PointsReservationHandler.ApplyAsync(
            paymentRepository, PointsReservationMapper.ToCommand(evt.Detail));

        await consumer.ConsumeAsync(evt.Id, [DynamoPaymentRepository.ToTransactWriteItem(payment)]);
    }

    [LambdaFunction]
    public async Task PointsReservationFailedConsumer(
        EventBridgeEvent<PointsReservationFailedEvent> evt,
        [FromServices] IPaymentRepository paymentRepository,
        [FromServices] IIdempotentEventConsumer consumer)
    {
        var payment = await PointsReservationHandler.ApplyAsync(
            paymentRepository, PointsReservationMapper.ToCommand(evt.Detail));

        await consumer.ConsumeAsync(evt.Id, [DynamoPaymentRepository.ToTransactWriteItem(payment)]);
    }
}
