namespace Catalog.API.Models;

public class Product : Aggregate<ProductId>
{
    public static Product Create(
    ProductId id,
    string name,
    string description,
    string imageUrl,
    decimal price,
    List<Category> categories)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUrl);
        ArgumentNullException.ThrowIfNull(categories);

        if (categories.Count == 0)
            //TODO: Use a custom exception
            throw new ArgumentException("Categories cannot be empty.", nameof(categories));


        if (price <= 0)
            throw new ArgumentOutOfRangeException(nameof(price), "Price must be greater than zero.");

        var product = new Product
        {
            Id = id,
            Name = name,
            Description = description,
            ImageUrl = imageUrl,
            Price = price,
            Categories = categories
        };

        // TODO: Criar o consumidor desse evento
        product.AddDomainEvent(new ProductCreatedEvent(product));

        return product;
    }

    public void Update(
        string name,
        string description,
        string imageUrl,
        decimal price,
        List<Category> categories)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUrl);
        ArgumentNullException.ThrowIfNull(categories);

        if (categories.Count == 0)
            throw new ArgumentException("Categories cannot be empty.", nameof(categories));

        if (price <= 0)
            throw new ArgumentOutOfRangeException(nameof(price), "Price must be greater than zero.");

        Name = name;
        Description = description;
        ImageUrl = imageUrl;
        Price = price;
        Categories = categories;

        AddDomainEvent(new ProductUpdatedEvent(this));
    }

    public string Name { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public string ImageUrl { get; private set; } = default!;
    public decimal Price { get; private set; } = default!;

    public List<Category> Categories { get; private set; } = default!;
}

