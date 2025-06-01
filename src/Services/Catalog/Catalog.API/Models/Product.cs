namespace Catalog.API.Models;

public class Product : Aggregate<ProductId>
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

        // TODO: Criar o consumidor desse evento
        product.AddDomainEvent(new ProductCreatedEvent(product));

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

        AddDomainEvent(new ProductUpdatedEvent(this));
    }

    public string Name { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public string ImageUrl { get; private set; } = default!;
    public decimal Price { get; private set; } = default!;

    //TODO: criar eventos para controle de estoque
    public int Stock { get; private set; } = default!;
    public List<CategoryId> CategoryIds { get; private set; } = default!;
}
