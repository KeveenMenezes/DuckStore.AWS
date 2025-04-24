namespace Ordering.Domain.AggregatesModel.OrderAggregate.ValueObjects;

public record CustomerId
{
    public Guid Value { get; }
    public CustomerId(Guid value) => Value = value;

    public static CustomerId Of(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new CustomerIdBadRequestException(value);
        }

        return new CustomerId(value);
    }
}
