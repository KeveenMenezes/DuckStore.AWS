namespace CatalogView.Function.Modules.Products.Queries.SearchProducts;

// Payload contract for the AppSync `products(...)` Lambda resolver (ADR-0027 amends ADR-0009:
// products/product escalate from Direct to Lambda because OpenSearch is an external integration).
// PascalCase to match DefaultLambdaJsonSerializer, mirroring every other Lambda-invoked resolver
// payload in this codebase (see graphql/resolvers/*.js + app/api/graphql/local.ts).
public sealed record SearchProductsRequest
{
    public string? Query { get; init; }
    public string? SortBy { get; init; }
    public bool Descending { get; init; }
    public double? MinRating { get; init; }
    public double? MaxRating { get; init; }
    public int PageSize { get; init; } = 20;
    public string? NextToken { get; init; }
}

public sealed record ProductSearchItem
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

public sealed record SearchProductsResponse
{
    public List<ProductSearchItem> Items { get; init; } = [];
    public string? NextToken { get; init; }
}
