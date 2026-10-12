namespace Payment.Function.Shared.Exceptions;

// Thrown, not swallowed, by the points reservation consumers while the checkout saga's payment is
// still Registered: the checkout event that promotes it may simply not have been consumed yet, so
// the invocation fails and EventBridge retries it (ADR-0050 §6).
public class PaymentNotYetPromotedException(Guid orderId)
    : Exception($"The payment for order \"{orderId}\" is still Registered; the points outcome arrived before its promotion.");
