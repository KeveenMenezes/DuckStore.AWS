namespace Ordering.Domain.AggregatesModel.OrderAggregate.ValueObjects;

public record OrderId
{
    private OrderId(Guid value) => Value = value;

    public static OrderId Of(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new OrderIdBadRequestException(value);
        }

        return new OrderId(value);
    }

    public Guid Value { get; }
}
