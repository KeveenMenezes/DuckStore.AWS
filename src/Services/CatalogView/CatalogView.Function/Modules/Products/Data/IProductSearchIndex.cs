namespace CatalogView.Function.Modules.Products.Data;

public interface IProductSearchIndex
{
    Task UpsertAsync(SearchDocument document, CancellationToken cancellationToken = default);

    Task DeleteAsync(string productId, CancellationToken cancellationToken = default);

    // Rewrites a category's denormalized name on every product document that references it
    // (CategorySyncHandler, triggered by a category rename — ADR-0027 extension, ADR-0030).
    Task RenameCategoryAsync(string categoryId, string name, CancellationToken cancellationToken = default);

    // Idempotent two-step update (see DynamoProductIndex): a conditional ADD applies the rating
    // delta only if LastRatingEventId doesn't already match eventId, then a second read+recompute
    // sets AverageRating (ADR-0030 — DynamoDB can't divide two attributes in one UpdateExpression).
    Task ApplyRatingAsync(
        string productId, string eventId, int rating, CancellationToken cancellationToken = default);

    // Sibling to ApplyRatingAsync for the edit path (ADR-0029): RatingCount stays unchanged,
    // RatingSum moves by (newRating - oldRating). Same idempotency-marker guard.
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

    // Seeder-only operation (CatalogView.DevelopmentDataSeeder): bulk-writes the historical
    // backfill via BatchWriteItem, chunked to DynamoDB's 25-item-per-call limit. A full-document
    // write is safe here because it runs once, before any steady-state traffic (ADR-0027 §"Historical
    // backfill", ADR-0030).
    Task BulkIndexAsync(IEnumerable<SearchDocument> documents, CancellationToken cancellationToken = default);
}
