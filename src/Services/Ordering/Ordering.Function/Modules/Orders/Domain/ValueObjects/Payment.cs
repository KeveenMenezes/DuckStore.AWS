namespace Ordering.Function.Modules.Orders.Domain.ValueObjects;

public class Payment : ValueObject
{
    public string? CardName { get; } = default!;
    public string CardNumber { get; } = default!;
    public string Expiration { get; } = default!;
    public string Cvv { get; } = default!;
    public PaymentMethod PaymentMethod { get; } = default!;
    public int Installments { get; } = default!;

    protected Payment()
    {
    }

    private Payment(
        string cardName,
        string cardNumber,
        string expiration,
        string cvv,
        PaymentMethod paymentMethod,
        int installments)
    {
        CardName = cardName;
        CardNumber = cardNumber;
        Expiration = expiration;
        Cvv = cvv;
        PaymentMethod = paymentMethod;
        Installments = installments;
    }

    public static Payment Of(
        string cardName,
        string cardNumber,
        string expiration,
        string cvv,
        PaymentMethod paymentMethod,
        int installments)
    {
        // Cash carries no card at all — only Card payments (Debit/Credit) require one.
        if (paymentMethod != PaymentMethod.Cash)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(cardName);
            ArgumentException.ThrowIfNullOrWhiteSpace(cardNumber);
            ArgumentException.ThrowIfNullOrWhiteSpace(cvv);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(cvv.Length, 3);
        }

        return new Payment(cardName, cardNumber, expiration, cvv, paymentMethod, installments);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CardName;
        yield return CardNumber;
        yield return Expiration;
        yield return Cvv;
        yield return PaymentMethod;
        yield return Installments;
    }
}
