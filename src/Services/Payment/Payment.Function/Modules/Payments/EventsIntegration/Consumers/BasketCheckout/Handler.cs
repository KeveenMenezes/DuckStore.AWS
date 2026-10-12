namespace Payment.Function.Modules.Payments.EventsIntegration.Consumers.BasketCheckout;

public static class PromotePaymentHandler
{
    // The payment write the checkout event commits with its inbox record. The checkout saga
    // registered the payment keyed by OrderId (ADR-0050 §6): promote it, conditioned on the status it
    // was read in. A missing payment throws so EventBridge retries (then DLQ) — every checkout
    // registers one before the cart closes. Any other status (already promoted) writes nothing —
    // only the inbox.
    public static async Task<IReadOnlyList<TransactWriteItem>> ToPaymentWritesAsync(
        IPaymentRepository paymentRepository,
        PromotePaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        var registered = await paymentRepository.GetByIdAsync(PaymentId.Of(command.OrderId).Value, cancellationToken)
            ?? throw new PaymentForOrderNotFoundException(command.OrderId);

        // Conditioned on the status it was read in, so a concurrent change cancels the transaction
        // and EventBridge retries against the new state.
        var readStatus = registered.Status;
        return registered.Promote(awaitingPoints: command.PointsToUse > 0)
            ? [DynamoPaymentRepository.ToTransactWriteItem(registered, expectedStatus: readStatus)]
            : [];
    }
}
