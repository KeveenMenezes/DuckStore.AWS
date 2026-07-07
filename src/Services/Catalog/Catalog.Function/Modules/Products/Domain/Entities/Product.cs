namespace Catalog.Function.Modules.Products.Domain.Entities;

public class Product : Aggregate<ProductId>
{
    public static Product Create(
    Guid id,
    string name,
    string description,
    string imageUrl,
    int stock,
    List<CategoryId> categoryIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUrl);
        ArgumentNullException.ThrowIfNull(categoryIds);

        if (categoryIds.Count == 0)
            throw new ArgumentException("Categories cannot be empty.", nameof(categoryIds));

        var product = new Product
        {
            Id = ProductId.Of(id),
            Name = name,
            Description = description,
            ImageUrl = imageUrl,
            Stock = stock,
            CategoryIds = categoryIds
        };

        return product;
    }

    public void Update(
        string name,
        string description,
        string imageUrl,
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

        Name = name;
        Description = description;
        ImageUrl = imageUrl;
        Stock = stock;

        CategoryIds = categoryIds;
    }

    public string Name { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public string ImageUrl { get; private set; } = default!;
    public int Stock { get; private set; } = default!;
    public List<CategoryId> CategoryIds { get; private set; } = default!;

    // Rating aggregation (AverageRating/RatingCount) moved to the CatalogView service, backed by
    // OpenSearch — it is no longer materialized on the product item (ADR-0027, supersedes ADR-0011
    // §4). Catalog now owns only the product's own write-side fields.
    internal static Product Load(
        Guid id,
        string name,
        string description,
        string imageUrl,
        int stock,
        List<CategoryId> categoryIds) =>
        new()
        {
            Id = ProductId.Of(id),
            Name = name,
            Description = description,
            ImageUrl = imageUrl,
            Stock = stock,
            CategoryIds = categoryIds
        };
}
