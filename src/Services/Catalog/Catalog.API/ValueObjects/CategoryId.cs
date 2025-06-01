namespace Catalog.API.ValueObjects;

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
}
