namespace Catalog.API.ValueObjects;

[JsonConverter(typeof(ProductAttributeConverter))]
public record ProductAttribute
{
    public string Name { get; init; } = default!;
    public string Unit { get; init; } = default!;
    public AttributeType Type { get; init; } = default!;
    public bool IsRequired { get; init; } = default!;

    public ProductAttribute(
        string name,
        string unit,
        AttributeType type,
        bool isRequired = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);

        if (type == AttributeType.Undefined)
            throw new ArgumentException(
                "Attribute type cannot be undefined.",
                nameof(type));

        if (unit.Length > 10)
            throw new ArgumentException(
                "Unit cannot exceed 10 characters.",
                nameof(unit));

        Name = name;
        Unit = unit;
        Type = type;
        IsRequired = isRequired;
    }

    public static ProductAttribute Of(
        string name,
        AttributeType type,
        string unit,
        bool isRequired = false)
    {
        return new ProductAttribute(name, unit, type, isRequired);
    }
}
