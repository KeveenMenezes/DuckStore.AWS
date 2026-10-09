using BuildingBlocks.Messaging.Serialization;
namespace CatalogView.Function.Modules.Products.EventsIntegration.Consumers.ReviewSync.Strategies;

// Consumes ReviewDeletedEvent (ADR-0049 — a customer withdrawing a Published review) and subtracts
// the withdrawn rating from the product's catalogview-products item: ratingCount-1,
// ratingSum -= rating, the rating's histogram bucket -1, floored at zero, then a recompute of
// AverageRating. Sibling to ReviewCreateStrategy/ReviewUpdateStrategy, sharing their
// LastRatingEventId idempotency marker (ADR-0030).
public sealed class ReviewDeleteStrategy(IProductSearchIndex index) : IReviewSyncStrategy
{
    public bool CanHandle(string detailType) => detailType == nameof(ReviewDeletedEvent);

    public Task HandleAsync(string eventId, JsonElement detail, CancellationToken cancellationToken = default)
    {
        var evt = detail.Deserialize(MessagingSerializerContext.Default.ReviewDeletedEvent)!;
        return index.ApplyRatingRemovalAsync(evt.ProductId.ToString(), eventId, evt.Rating, cancellationToken);
    }
}
