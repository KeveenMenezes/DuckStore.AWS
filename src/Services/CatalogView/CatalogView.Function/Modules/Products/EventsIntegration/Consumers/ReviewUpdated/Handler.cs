using CatalogView.Function.Modules.Products.Data;

namespace CatalogView.Function.Modules.Products.EventsIntegration.Consumers.ReviewUpdated;

// Consumes ReviewUpdated (ADR-0029 — a customer editing their existing review, enabled by the
// composite Id in the reviews table) and folds the rating delta into the product's OpenSearch
// document via a single atomic, idempotent scripted update: ratingCount stays unchanged, ratingSum
// moves by (NewRating - OldRating). Sibling to ReviewAggregateHandler, which handles brand-new
// reviews (ratingCount+1) — see ADR-0027 for the shared idempotency-marker mechanism.
public sealed class ReviewUpdateAggregateHandler(IProductSearchIndex index)
{
    public Task HandleAsync(string eventId, ReviewUpdatedEvent evt, CancellationToken cancellationToken = default) =>
        index.ApplyRatingUpdateAsync(evt.ProductId.ToString(), eventId, evt.OldRating, evt.NewRating, cancellationToken);
}
