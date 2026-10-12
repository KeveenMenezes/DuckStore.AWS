namespace Payment.Function.Modules.Payments.EventsIntegration.Consumers.PointsReservation;

public static class PointsReservationHandler
{
    // Rehydrates the order's payment and applies the reservation outcome in memory, without
    // persisting — the endpoint writes it inside the idempotency inbox transaction, like PaymentResult.
    //
    // Unlike PaymentResult, a missing payment throws instead of returning: Payment and Challenges
    // consume BasketCheckoutEvent in parallel, so the outcome can arrive before the payment row is
    // written. Throwing lets EventBridge retry (then DLQ); dropping it would leave the payment
    // AwaitingPoints forever (ADR-0048 §6). The same holds for a payment the checkout saga
    // registered but the checkout event hasn't promoted yet: ConfirmPoints/RejectPoints throw
    // PaymentNotYetPromotedException (ADR-0050 §6).
    public static async Task<Domain.Entities.Payment> ApplyAsync(
        IPaymentRepository paymentRepository,
        ApplyPointsReservationCommand command,
        CancellationToken cancellationToken = default)
    {
        var payment = await paymentRepository.GetByOrderIdAsync(command.OrderId, cancellationToken)
            ?? throw new PaymentForOrderNotFoundException(command.OrderId);

        if (command.Reserved)
            payment.ConfirmPoints();
        else
            payment.RejectPoints();

        return payment;
    }
}
