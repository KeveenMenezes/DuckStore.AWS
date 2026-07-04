namespace Ordering.Function.Modules.Orders.Domain.ValueObjects;

public class OrderItemId : ValueObject<Guid>
{
    private OrderItemId(Guid value) : base(value) { }

    public static OrderItemId Of(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new OrderIdBadRequestException(value);
        }

        return new OrderItemId(value);
    }
}
