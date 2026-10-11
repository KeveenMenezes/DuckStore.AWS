using Review.Function.Modules.Reviews.Domain.Enums;

namespace Review.Function.Modules.Reviews.EventsIntegration.Publishers.Rules;

// Fires when a Published review (legacy rows without Status included) is withdrawn by
// deleteReview (Published -> Deleted) and publishes ReviewDeleted with the old rating, so
// CatalogView subtracts it once. The TTL REMOVE that later erases the Deleted row has no new
// image and never matches — the aggregate was already adjusted here (ADR-0049 §5).
public sealed class ReviewDeletedRule : IStreamRule<ReviewStreamImage>
{
    public bool Match(StreamContext<ReviewStreamImage> context) =>
        context.Old is { EffectiveStatus: ReviewStatus.Published }
        && context.New is { EffectiveStatus: ReviewStatus.Deleted };

    public Task<PublishInstruction> BuildAsync(
        StreamContext<ReviewStreamImage> context, CancellationToken cancellationToken = default)
    {
        var oldReview = context.Old!;

        return Task.FromResult(new PublishInstruction(
            nameof(ReviewDeletedEvent),
            new ReviewDeletedEvent
            {
                ReviewId = oldReview.Id,
                ProductId = oldReview.ProductId,
                Rating = oldReview.Rating
            }));
    }
}
