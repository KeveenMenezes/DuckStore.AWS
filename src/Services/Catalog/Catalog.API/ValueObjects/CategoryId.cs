namespace Catalog.API.ValueObjects;

[JsonConverter(typeof(CategoryIdConverter))]
public record CategoryId
{
    public Guid Value { get; }
    public CategoryId(Guid value) => Value = value;

    public static CategoryId Of(Guid value)
    {
        if (value == Guid.Empty)
        {
            // TODO: Create a specific exception for CategoryId
            throw new ArgumentException("CategoryId cannot be empty.", nameof(value));
        }

        return new CategoryId(value);
    }

    public static List<CategoryId> Of(IEnumerable<Guid> values)
    {
        if (values == null || !values.Any())
        {
            //TODO: Create a specific exception for CategoryId
            throw new ArgumentException("Values cannot be null or empty.", nameof(values));
        }

        return [.. values.Select(Of)];
    }
}
