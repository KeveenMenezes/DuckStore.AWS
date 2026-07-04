namespace Ordering.Function.Modules.Orders.Domain.ValueObjects;

public class OrderName : ValueObject<string>
{
    private OrderName(string value) : base(value) { }

    public static OrderName Of(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return new OrderName(value);
    }
}
