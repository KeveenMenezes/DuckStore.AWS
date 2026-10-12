namespace Payment.Function.Shared.Exceptions;

// Thrown, not swallowed, by the points reservation consumers: the payment may simply not be written
// yet, so the invocation fails and EventBridge retries it (ADR-0048 §6).
public class PaymentForOrderNotFoundException(Guid orderId)
    : NotFoundException("Payment for order", orderId);
