namespace Payment.Function;

// The checkout saga's compensation when the cart changed after the payment was registered
// (ADR-0050 §1/§6). Voided is false when there was nothing to void (already voided, promoted or absent).
public record VoidPaymentRequest(Guid OrderId);

public record VoidPaymentResponse(bool Voided);
