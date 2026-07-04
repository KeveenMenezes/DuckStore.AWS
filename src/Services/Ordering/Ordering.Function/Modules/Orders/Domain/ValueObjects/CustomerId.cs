namespace Ordering.Function.Modules.Orders.Domain.ValueObjects;

public class CustomerId : ValueObject<Guid>
{
    private CustomerId(Guid value) : base(value) { }

    public static CustomerId Of(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new CustomerIdBadRequestException(value);
        }

        return new CustomerId(value);
    }
}
