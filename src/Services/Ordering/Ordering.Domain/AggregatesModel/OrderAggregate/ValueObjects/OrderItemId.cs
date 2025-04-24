namespace Ordering.Domain.AggregatesModel.OrderAggregate.ValueObjects;

public record OrderItemId
{
    public Guid Value { get; }
    private OrderItemId(Guid value) => Value = value;
    public static OrderItemId Of(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new OrderIdBadRequestException(value);
        }

        return new OrderItemId(value);
    }
}
