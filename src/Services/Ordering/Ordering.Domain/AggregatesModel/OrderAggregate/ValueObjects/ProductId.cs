namespace Ordering.Domain.AggregatesModel.OrderAggregate.ValueObjects;

public record ProductId
{
    public Guid Value { get; }
    private ProductId(Guid value) => Value = value;

    public static ProductId Of(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ProductIdBadRequestException(value);
        }

        return new ProductId(value);
    }
}
