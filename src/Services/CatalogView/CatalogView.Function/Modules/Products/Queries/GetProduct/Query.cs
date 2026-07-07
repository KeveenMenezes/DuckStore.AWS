namespace CatalogView.Function.Modules.Products.Queries.GetProduct;

// Payload contract for the AppSync `product(id)` Lambda resolver (ADR-0027 amends ADR-0009).
public sealed record GetProductRequest
{
    public string Id { get; init; } = string.Empty;
}

public sealed record GetProductResponse
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ImageUrl { get; init; } = string.Empty;
    public decimal OriginalPrice { get; init; }
    public int Stock { get; init; }
    public List<string> CategoryIds { get; init; } = [];
    public double AverageRating { get; init; }
    public int RatingCount { get; init; }
    public decimal Price { get; init; }
    public decimal CashPrice { get; init; }
    public int MaxInstallmentsWithoutInterest { get; init; }
    public decimal MaxInstallmentValue { get; init; }
}
