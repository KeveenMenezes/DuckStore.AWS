namespace Catalog.API.ValueObjects;

[JsonConverter(typeof(ProductIdConverter))]
public record ProductId
{
    public Guid Value { get; }
    public ProductId(Guid value) => Value = value;

    public static ProductId Of(Guid value)
    {
        if (value == Guid.Empty)
        {
            // TODO: Create a specific exception for ProductId
            throw new ArgumentException(" ProductId cannot be empty.", nameof(value));
        }

        return new ProductId(value);
    }
}
