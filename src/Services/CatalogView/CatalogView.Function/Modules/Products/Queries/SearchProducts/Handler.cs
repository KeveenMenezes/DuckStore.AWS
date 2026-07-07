using CatalogView.Function.Modules.Products.Data;
using CatalogView.Function.Modules.Products.Domain;

namespace CatalogView.Function.Modules.Products.Queries.SearchProducts;

public sealed class SearchProductsHandler(IProductSearchIndex index)
{
    public async Task<SearchProductsResponse> HandleAsync(
        SearchProductsRequest request, CancellationToken cancellationToken = default)
    {
        var criteria = new ProductSearchCriteria(
            request.Query,
            ParseSortField(request.SortBy),
            request.Descending,
            request.MinRating,
            request.MaxRating,
            request.PageSize <= 0 ? 20 : request.PageSize,
            request.NextToken);

        var result = await index.SearchAsync(criteria, cancellationToken);

        return new SearchProductsResponse
        {
            Items = [.. result.Items.Select(ToItem)],
            NextToken = result.NextToken
        };
    }

    private static ProductSortField ParseSortField(string? sortBy) =>
        Enum.TryParse<ProductSortField>(sortBy, ignoreCase: true, out var parsed)
            ? parsed
            : ProductSortField.Relevance;

    private static ProductSearchItem ToItem(SearchDocument document) =>
        new()
        {
            Id = document.Id,
            Name = document.Name,
            Description = document.Description,
            ImageUrl = document.ImageUrl,
            OriginalPrice = document.OriginalPrice,
            Stock = document.Stock,
            CategoryIds = document.CategoryIds,
            AverageRating = document.AverageRating,
            RatingCount = document.RatingCount,
            Price = document.Price,
            CashPrice = document.CashPrice,
            MaxInstallmentsWithoutInterest = document.MaxInstallmentsWithoutInterest,
            MaxInstallmentValue = document.MaxInstallmentValue
        };
}
