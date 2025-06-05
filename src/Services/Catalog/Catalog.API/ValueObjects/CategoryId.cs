namespace Catalog.API.ValueObjects;

public class CategoryId : ValueObject<Guid>
{
    private CategoryId(Guid value) : base(value) { }

    public static CategoryId Of(Guid value)
    {
        if (value == Guid.Empty)
        {
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
