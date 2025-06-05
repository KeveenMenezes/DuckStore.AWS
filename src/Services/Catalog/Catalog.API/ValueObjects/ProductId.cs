namespace Catalog.API.ValueObjects;

public class ProductId : ValueObject<Guid>
{
    [JsonConstructor]
    private ProductId(Guid value) : base(value) { }

    public static ProductId Of(Guid value)
    {
        if (value == Guid.Empty)
        {
            // TODO: Create a specific exception for ProductId
            throw new ArgumentException("ProductId cannot be empty.", nameof(value));
        }

        return new ProductId(value);
    }
}
