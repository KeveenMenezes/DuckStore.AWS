using Payment.Function.Modules.Payments.Domain.Services;

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

    // Structural only: also rehydrates stored payments and the legacy checkout event, whose
    // expirations predate the MM/YYYY rule. New checkouts go through Validated.
    public static CardDetails Of(string cardNumber, string expiration, string cvv, PaymentMethod paymentMethod)
    {
        // Cash carries no card at all — only Card payments (Debit/Credit) require one.
        if (paymentMethod != PaymentMethod.Cash)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(cardNumber);
            ArgumentException.ThrowIfNullOrWhiteSpace(cvv);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(cvv.Length, 4);
        }

        return new CardDetails(cardNumber, expiration, cvv, paymentMethod);
    }

    // The checkout's card rules (ADR-0050 §6): for card methods, a Luhn-valid number, an MM/YYYY
    // expiration that is not past (a card is valid through its expiration month) and a 3–4 digit
    // CVV. Cash needs no card.
    public static CardDetails Validated(
        string cardNumber, string expiration, string cvv, PaymentMethod paymentMethod, TimeProvider clock)
    {
        if (!Enum.IsDefined(paymentMethod))
            throw new ArgumentOutOfRangeException(nameof(paymentMethod), paymentMethod, "Unknown payment method.");

        if (paymentMethod != PaymentMethod.Cash)
        {
            if (!Luhn.IsValid(cardNumber))
                throw new InvalidCardException(nameof(CardNumber), "The card number is invalid.");

            if (!IsCurrentExpiration(expiration, clock.GetUtcNow()))
                throw new InvalidCardException(nameof(Expiration), "The card is expired or its expiration is not MM/YYYY.");

            if (cvv is not { Length: 3 or 4 } || !cvv.All(char.IsAsciiDigit))
                throw new InvalidCardException(nameof(Cvv), "The CVV must have 3 or 4 digits.");
        }

        return Of(cardNumber, expiration, cvv, paymentMethod);
    }

    private static bool IsCurrentExpiration(string expiration, DateTimeOffset now)
    {
        if (expiration is not { Length: 7 } || expiration[2] != '/')
            return false;

        if (!int.TryParse(expiration.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            || !int.TryParse(expiration.AsSpan(3, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || month is < 1 or > 12)
            return false;

        return (year, month).CompareTo((now.Year, now.Month)) >= 0;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CardNumber;
        yield return Expiration;
        yield return Cvv;
        yield return PaymentMethod;
    }
}
