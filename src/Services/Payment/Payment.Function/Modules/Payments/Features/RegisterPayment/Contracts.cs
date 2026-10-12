namespace Payment.Function;

// Invoked only by the checkout saga (ADR-0050 §2/§6). Card fields are null for cash.
public record RegisterPaymentRequest(
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    int PaymentMethod,
    int Installments,
    string? CardName,
    string? CardNumber,
    string? Expiration,
    string? Cvv)
{
    // Redacted: Lambda and MediatR logging print whole records, and card data must never reach a
    // log (ADR-0050 §7).
    protected virtual bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append($"OrderId = {OrderId}, CustomerId = {CustomerId}, Amount = {Amount}, ");
        builder.Append($"PaymentMethod = {PaymentMethod}, Installments = {Installments}, Card = [redacted]");
        return true;
    }
}

// Refusal is a typed outcome returned as data, never a thrown Lambda error, so the saga can map it
// to its errorType (ADR-0050 §8): InvalidCard (with the offending Field) or CheckoutRetryRequired.
public record RegisterPaymentResponse(Guid? PaymentId, PaymentRefusalResponse? Refusal = null);

public record PaymentRefusalResponse(string ErrorType, string Message, string? Field = null);

public static class PaymentRefusals
{
    public const string InvalidCard = "InvalidCard";
    public const string CheckoutRetryRequired = "CheckoutRetryRequired";
}
