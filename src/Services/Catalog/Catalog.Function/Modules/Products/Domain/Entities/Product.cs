namespace Catalog.Function.Modules.Products.Domain.Entities;

public class Product : Aggregate<ProductId>
{
    public static Product Create(
    Guid id,
    string name,
    string description,
    List<ProductImage> images,
    int stock,
    List<CategoryId> categoryIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ValidateImages(images);
        ArgumentNullException.ThrowIfNull(categoryIds);

        if (categoryIds.Count == 0)
            throw new ArgumentException("Categories cannot be empty.", nameof(categoryIds));

        var product = new Product
        {
            Id = ProductId.Of(id),
            Name = name,
            Description = description,
            Images = images,
            Stock = stock,
            CategoryIds = categoryIds
        };

        return product;
    }

    public void Update(
        string name,
        string description,
        List<ProductImage> images,
        int stock,
        List<CategoryId> categoryIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ValidateImages(images);
        ArgumentNullException.ThrowIfNull(categoryIds);

        if (categoryIds.Count == 0)
            throw new ArgumentException(
                "Categories cannot be empty.",
                nameof(categoryIds));

        Name = name;
        Description = description;
        Images = images;
        Stock = stock;

        CategoryIds = categoryIds;
    }

    // Empty is valid (products created before the image pipeline render a placeholder), but a
    // non-empty list must elect exactly one main image (ADR-0034).
    private static void ValidateImages(List<ProductImage> images)
    {
        ArgumentNullException.ThrowIfNull(images);

        if (images.Count > 0 && images.Count(i => i.IsMain) != 1)
            throw new ArgumentException("Exactly one image must be marked as main.", nameof(images));
    }

    public string Name { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public List<ProductImage> Images { get; private set; } = [];
    public int Stock { get; private set; } = default!;
    public List<CategoryId> CategoryIds { get; private set; } = default!;

    // Rating aggregation (AverageRating/RatingCount) moved to the CatalogView service — it is no
    // longer materialized on the product item (ADR-0027, supersedes ADR-0011 §4; ADR-0030).
    // Catalog now owns only the product's own write-side fields.
    internal static Product Load(
        Guid id,
        string name,
        string description,
        List<ProductImage> images,
        int stock,
        List<CategoryId> categoryIds) =>
        new()
        {
            Id = ProductId.Of(id),
            Name = name,
            Description = description,
            Images = images,
            Stock = stock,
            CategoryIds = categoryIds
        };
}
