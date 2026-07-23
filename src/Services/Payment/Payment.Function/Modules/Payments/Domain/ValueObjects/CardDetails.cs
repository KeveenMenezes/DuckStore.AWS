namespace Payment.Function.Modules.Payments.Domain.ValueObjects;

public class CardDetails : ValueObject
{
    public string CardNumber { get; } = default!;
    public string Expiration { get; } = default!;
    public string Cvv { get; } = default!;
    public PaymentMethod PaymentMethod { get; } = default!;

    protected CardDetails()
    {
    }

    private CardDetails(string cardNumber, string expiration, string cvv, PaymentMethod paymentMethod)
    {
        CardNumber = cardNumber;
        Expiration = expiration;
        Cvv = cvv;
        PaymentMethod = paymentMethod;
    }

    public static CardDetails Of(string cardNumber, string expiration, string cvv, PaymentMethod paymentMethod)
    {
        // Cash carries no card at all — only Card payments (Debit/Credit) require one.
        if (paymentMethod != PaymentMethod.Cash)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(cardNumber);
            ArgumentException.ThrowIfNullOrWhiteSpace(cvv);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(cvv.Length, 3);
        }

        return new CardDetails(cardNumber, expiration, cvv, paymentMethod);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CardNumber;
        yield return Expiration;
        yield return Cvv;
        yield return PaymentMethod;
    }
}
