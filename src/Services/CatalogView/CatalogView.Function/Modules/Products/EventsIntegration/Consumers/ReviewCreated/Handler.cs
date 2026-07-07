using CatalogView.Function.Modules.Products.Data;

namespace CatalogView.Function.Modules.Products.EventsIntegration.Consumers.ReviewCreated;

// Consumes ReviewCreated (ADR-0011, still owned/published by Review) and folds the new rating
// into the product's OpenSearch document via a single atomic, idempotent scripted update
// (ADR-0027) — replacing Catalog's old two-step GetItem+UpdateItem approach. Idempotency is
// native to OpenSearch (lastRatingEventId field on the document itself), so no DynamoDB inbox
// table is needed here.
public sealed class ReviewAggregateHandler(IProductSearchIndex index)
{
    public Task HandleAsync(string eventId, ReviewCreatedEvent evt, CancellationToken cancellationToken = default) =>
        index.ApplyRatingAsync(evt.ProductId.ToString(), eventId, evt.Rating, cancellationToken);
}
