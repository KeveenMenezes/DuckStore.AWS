using Review.Function.Modules.Reviews.Domain.Enums;

namespace Review.Function.Modules.Reviews.EventsIntegration.Publishers.Rules;

// Fires on the transition into Published, not on row existence (ADR-0049 §5): an Eligible row
// created by the OrderCompleted consumer isn't a public review and must not touch the average.
// Eligible -> Published (first publication) and Deleted -> Published (re-publication) both
// publish ReviewCreated so CatalogView folds the rating in as a fresh data point: ratingCount+1,
// ratingSum += rating (ADR-0011). An INSERT of a legacy row without Status counts as Published.
public sealed class ReviewCreatedRule : IStreamRule<ReviewStreamImage>
{
    public bool Match(StreamContext<ReviewStreamImage> context) =>
        context.New is { EffectiveStatus: ReviewStatus.Published }
        && context.Old?.EffectiveStatus is null or ReviewStatus.Eligible or ReviewStatus.Deleted;

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
                UserId = review.UserId,
                Rating = review.Rating
            }));
    }
}
