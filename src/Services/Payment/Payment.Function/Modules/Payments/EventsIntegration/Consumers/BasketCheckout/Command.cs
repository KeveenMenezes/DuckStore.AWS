namespace Payment.Function.Modules.Payments.EventsIntegration.Consumers.BasketCheckout;

// What the checkout consumer needs from BasketCheckoutEvent: whose registered payment to promote and
// whether it waits for the points reservation. The card never travels on the event (ADR-0050 §7).
public record PromotePaymentCommand(Guid OrderId, int PointsToUse);
