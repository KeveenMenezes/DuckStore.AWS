using BuildingBlocks.Messaging.Serialization;
namespace CatalogView.Function.Modules.Products.EventsIntegration.Consumers.ReviewSync.Strategies;

// Consumes ReviewCreatedEvent (ADR-0011, still owned/published by Review) and folds the new rating
// into the product's catalogview-products item via a two-step, idempotent DynamoDB update
// (ADR-0030) — a conditional ADD guarded by LastRatingEventId, then a recompute of AverageRating.
// No separate inbox table is needed: the idempotency marker lives on the item itself.
public sealed class ReviewCreateStrategy(IProductSearchIndex index) : IReviewSyncStrategy
{
    public bool CanHandle(string detailType) => detailType == nameof(ReviewCreatedEvent);

    public Task HandleAsync(string eventId, JsonElement detail, CancellationToken cancellationToken = default)
    {
        var evt = detail.Deserialize(MessagingSerializerContext.Default.ReviewCreatedEvent)!;
        return index.ApplyRatingAsync(evt.ProductId.ToString(), eventId, evt.Rating, cancellationToken);
    }
}
