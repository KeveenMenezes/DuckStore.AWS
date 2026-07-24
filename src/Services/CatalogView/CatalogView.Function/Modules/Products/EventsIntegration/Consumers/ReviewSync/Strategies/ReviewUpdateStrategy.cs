namespace CatalogView.Function.Modules.Products.EventsIntegration.Consumers.ReviewSync.Strategies;

// Consumes ReviewUpdatedEvent (ADR-0029 — a customer editing their existing review, enabled by the
// composite Id in the reviews table) and folds the rating delta into the product's
// catalogview-products item via the same two-step, idempotent DynamoDB update (ADR-0030):
// ratingCount stays unchanged, ratingSum moves by (NewRating - OldRating). Sibling to
// ReviewCreateStrategy, which handles brand-new reviews (ratingCount+1) — see DynamoProductIndex
// for the shared idempotency-marker mechanism.
public sealed class ReviewUpdateStrategy(IProductSearchIndex index) : IReviewSyncStrategy
{
    public bool CanHandle(string detailType) => detailType == nameof(ReviewUpdatedEvent);

    public Task HandleAsync(string eventId, JsonElement detail, CancellationToken cancellationToken = default)
    {
        var evt = detail.Deserialize<ReviewUpdatedEvent>()!;
        return index.ApplyRatingUpdateAsync(
            evt.ProductId.ToString(), eventId, evt.OldRating, evt.NewRating, cancellationToken);
    }
}
