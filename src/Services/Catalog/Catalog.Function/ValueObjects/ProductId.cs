namespace Catalog.API.ValueObjects;

public class ProductId : ValueObject<Guid>
{
    [JsonConstructor]
    private ProductId(Guid value) : base(value) { }

    public static ProductId Of(Guid value)
    {
        return value == Guid.Empty ? throw
            // TODO: Create a specific exception for ProductId
            new ArgumentException("ProductId cannot be empty.", nameof(value)) : new ProductId(value);
    }
}
