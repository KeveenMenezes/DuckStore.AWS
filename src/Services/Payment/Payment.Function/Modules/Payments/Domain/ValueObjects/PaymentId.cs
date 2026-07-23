namespace Payment.Function.Modules.Payments.Domain.ValueObjects;

public class PaymentId : ValueObject<Guid>
{
    private PaymentId(Guid value) : base(value) { }

    public static PaymentId Of(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new PaymentIdBadRequestException(value);
        }

        return new PaymentId(value);
    }
}
