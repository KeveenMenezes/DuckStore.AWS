namespace Ordering.Domain.ValueObjects;

public record OrderItemId
{
    public Guid Value { get; }
    private OrderItemId(Guid value) => Value = value;
    public static OrderItemId Of(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new OrderItemCoreException(OrderItemCoreError.OrderItemIdNotEmpty);
        }

        return new OrderItemId(value);
    }
}
