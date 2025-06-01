namespace Catalog.API.ValueObjects;

[JsonConverter(typeof(BrandIdConverter))]
public record BrandId
{
    public Guid Value { get; }

    public BrandId(Guid value) => Value = value;

    public static BrandId Of(Guid value)
    {
        if (value == Guid.Empty)
        {
            // TODO: Create a specific exception for BrandId
            throw new ArgumentException("BrandId cannot be empty.", nameof(value));
        }

        return new BrandId(value);
    }
}
