namespace Payment.Function.Modules.Payments.EventsIntegration.Consumers.BasketCheckout;

public static class BasketCheckoutMapper
{
    // A malformed payload throws, so EventBridge retries it into the DLQ.
    public static PromotePaymentCommand ToPromotePaymentCommand(BasketCheckoutEvent message) =>
        message.OrderId == Guid.Empty
            ? throw new ArgumentException("BasketCheckoutEvent carries no OrderId.", nameof(message))
            : new PromotePaymentCommand(message.OrderId, message.PointsToUse);
}
