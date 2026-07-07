namespace CatalogView.Function.Modules.Products.Data;

public enum ProductSortField { Relevance, Price, AverageRating }

public sealed record ProductSearchCriteria(
    string? Query,
    ProductSortField SortBy,
    bool Descending,
    double? MinRating,
    double? MaxRating,
    int PageSize,
    string? NextToken);

public sealed record ProductSearchResult(IReadOnlyList<SearchDocument> Items, string? NextToken);

public interface IProductSearchIndex
{
    Task UpsertAsync(SearchDocument document, CancellationToken cancellationToken = default);

    Task DeleteAsync(string productId, CancellationToken cancellationToken = default);

    // Atomic, idempotent Painless script update (see OpenSearchProductIndex): applies the rating
    // delta only if lastRatingEventId doesn't already match eventId (ADR-0027).
    Task ApplyRatingAsync(
        string productId, string eventId, int rating, CancellationToken cancellationToken = default);

    // Sibling to ApplyRatingAsync for the edit path (ADR-0029): ratingCount stays unchanged,
    // ratingSum moves by (newRating - oldRating). Same idempotency-marker guard.
    Task ApplyRatingUpdateAsync(
        string productId, string eventId, int oldRating, int newRating,
        CancellationToken cancellationToken = default);

    // Partial merge of price and payment-highlight fields, all driven by Pricing's single
    // PriceChangedEvent (ADR-0026/ADR-0028) — one event, one merge. Naturally idempotent: every
    // field is an absolute value, not a delta.
    Task ApplyPricingAsync(
        string productId,
        decimal originalPrice,
        decimal price,
        decimal cashPrice,
        int maxInstallmentsWithoutInterest,
        decimal maxInstallmentValue,
        CancellationToken cancellationToken = default);

    Task<SearchDocument?> GetAsync(string productId, CancellationToken cancellationToken = default);

    Task<ProductSearchResult> SearchAsync(
        ProductSearchCriteria criteria, CancellationToken cancellationToken = default);

    // Seeder-only operations (CatalogView.DevelopmentDataSeeder): create the index with an
    // explicit mapping if missing, and bulk-write the historical backfill. Both are full-document
    // writes (not the partial merge UpsertAsync uses), which is safe here because they run once,
    // before any steady-state traffic (ADR-0027 §"Historical backfill").
    Task EnsureIndexAsync(CancellationToken cancellationToken = default);

    Task BulkIndexAsync(IEnumerable<SearchDocument> documents, CancellationToken cancellationToken = default);
}
