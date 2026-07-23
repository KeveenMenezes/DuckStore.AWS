namespace Review.Function.Modules.Reviews.EventsIntegration.Publishers.Rules;

// Fires on a new review (INSERT) and publishes ReviewCreated so CatalogView folds the rating in
// as a fresh data point: ratingCount+1, ratingSum += rating (ADR-0011, still in effect).
public sealed class ReviewCreatedRule : IStreamRule<ReviewStreamImage>
{
    public bool Match(StreamContext<ReviewStreamImage> context) => context.EventName == "INSERT";

    public Task<PublishInstruction> BuildAsync(
        StreamContext<ReviewStreamImage> context, CancellationToken cancellationToken = default)
    {
        var review = context.New!;

        return Task.FromResult(new PublishInstruction(
            nameof(ReviewCreatedEvent),
            new ReviewCreatedEvent
            {
                ReviewId = review.Id,
                ProductId = review.ProductId,
                Rating = review.Rating
            }));
    }
}
