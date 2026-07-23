namespace Ordering.Function.Modules.Orders.Domain.ValueObjects;

// Card data (CardName/CardNumber/Expiration/Cvv) lives only in Payment.Function now — Order only
// keeps what it legitimately needs for its own receipt/order-history display (ADR-0038).
public class Payment : ValueObject
{
    public PaymentMethod PaymentMethod { get; } = default!;
    public int Installments { get; } = default!;

    protected Payment()
    {
    }

    private Payment(PaymentMethod paymentMethod, int installments)
    {
        PaymentMethod = paymentMethod;
        Installments = installments;
    }

    public static Payment Of(PaymentMethod paymentMethod, int installments) =>
        new(paymentMethod, installments);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return PaymentMethod;
        yield return Installments;
    }
}
