namespace Catalog.Function.Models;

public class Product : IdentifiableAggregate<ProductId, Guid>
{
    public static Product Create(
    Guid id,
    string name,
    string description,
    string imageUrl,
    decimal price,
    int stock,
    List<CategoryId> categoryIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUrl);
        ArgumentNullException.ThrowIfNull(categoryIds);

        if (categoryIds.Count == 0)
            //TODO: Use a custom exception
            throw new ArgumentException("Categories cannot be empty.", nameof(categoryIds));


        if (price <= 0)
            throw new ArgumentOutOfRangeException(nameof(price), "Price must be greater than zero.");

        var product = new Product
        {
            Id = ProductId.Of(id),
            Name = name,
            Description = description,
            ImageUrl = imageUrl,
            Price = price,
            Stock = stock,
            CategoryIds = categoryIds
        };

        return product;
    }

    public void Update(
        string name,
        string description,
        string imageUrl,
        decimal price,
        int stock,
        List<CategoryId> categoryIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUrl);
        ArgumentNullException.ThrowIfNull(categoryIds);

        if (categoryIds.Count == 0)
            throw new ArgumentException(
                "Categories cannot be empty.",
                nameof(categoryIds));

        if (price <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(price),
                "Price must be greater than zero.");

        Name = name;
        Description = description;
        ImageUrl = imageUrl;
        Price = price;
        Stock = stock;

        CategoryIds = categoryIds;
    }

    public string Name { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public string ImageUrl { get; private set; } = default!;
    public decimal Price { get; private set; } = default!;

    //TODO: emit events for stock control
    public int Stock { get; private set; } = default!;
    public List<CategoryId> CategoryIds { get; private set; } = default!;

    // Reconstitutes an already-persisted Product (without re-validating creation rules or raising domain events).
    internal static Product Load(
        Guid id,
        string name,
        string description,
        string imageUrl,
        decimal price,
        int stock,
        List<CategoryId> categoryIds) =>
        new()
        {
            Id = ProductId.Of(id),
            Name = name,
            Description = description,
            ImageUrl = imageUrl,
            Price = price,
            Stock = stock,
            CategoryIds = categoryIds
        };
}
