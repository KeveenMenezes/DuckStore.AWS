namespace Catalog.API.Models;

public class Brand : Entity<BrandId>
{
    public string Name { get; private set; } = default!;

    public static Brand Create(BrandId id, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var brand = new Brand
        {
            Id = id,
            Name = name
        };

        return brand;
    }
}
