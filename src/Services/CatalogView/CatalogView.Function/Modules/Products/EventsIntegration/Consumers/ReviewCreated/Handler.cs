using CatalogView.Function.Modules.Products.Data;

namespace CatalogView.Function.Modules.Products.EventsIntegration.Consumers.ReviewCreated;

// Consumes ReviewCreated (ADR-0011, still owned/published by Review) and folds the new rating
// into the product's catalogview-products item via a two-step, idempotent DynamoDB update
// (ADR-0030) — a conditional ADD guarded by LastRatingEventId, then a recompute of
// AverageRating. No separate inbox table is needed: the
// idempotency marker lives on the item itself.
public sealed class ReviewAggregateHandler(IProductSearchIndex index)
{
    public Task HandleAsync(string eventId, ReviewCreatedEvent evt, CancellationToken cancellationToken = default) =>
        index.ApplyRatingAsync(evt.ProductId.ToString(), eventId, evt.Rating, cancellationToken);
}
