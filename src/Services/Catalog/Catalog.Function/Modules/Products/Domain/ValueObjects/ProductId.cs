namespace Catalog.Function.Modules.Products.Domain.ValueObjects;

public class ProductId : ValueObject<Guid>
{
    [JsonConstructor]
    private ProductId(Guid value) : base(value) { }

    public static ProductId Of(Guid value)
    {
        return value == Guid.Empty ? throw
            new ArgumentException("ProductId cannot be empty.", nameof(value)) : new ProductId(value);
    }
}
