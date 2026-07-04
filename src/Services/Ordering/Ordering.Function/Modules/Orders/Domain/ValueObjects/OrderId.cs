namespace Ordering.Function.Modules.Orders.Domain.ValueObjects;

public class OrderId : ValueObject<Guid>
{
    private OrderId(Guid value) : base(value) { }

    public static OrderId Of(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new OrderIdBadRequestException(value);
        }

        return new OrderId(value);
    }
}
