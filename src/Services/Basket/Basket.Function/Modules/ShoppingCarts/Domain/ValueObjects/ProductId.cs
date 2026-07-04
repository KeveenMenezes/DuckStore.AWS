namespace Basket.Function.Modules.ShoppingCarts.Domain.ValueObjects;

public class ProductId : ValueObject<Guid>
{
    private ProductId(Guid value) : base(value) { }

    public static ProductId Of(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ProductIdBadRequestException(value);
        }

        return new ProductId(value);
    }
}
