using Review.Function.Modules.Reviews.Domain.Enums;

namespace Review.Function.Modules.Reviews.EventsIntegration.Publishers.Rules;

// Fires on an edit of a review that stays Published (Published -> Published, legacy rows without
// Status included — ADR-0049 §5) and publishes ReviewUpdated so CatalogView applies a rating delta:
// ratingCount stays unchanged, ratingSum += (NewRating - OldRating) (ADR-0029).
//
// Matches even when the rating itself didn't change (a comment-only edit) — this event is also
// what drives ISR revalidation of the product page's review list, so a comment-only edit still
// needs to invalidate that cache. A zero rating delta is a harmless no-op on the CatalogView side.
public sealed class ReviewUpdatedRule : IStreamRule<ReviewStreamImage>
{
    public bool Match(StreamContext<ReviewStreamImage> context) =>
        context.Old is { EffectiveStatus: ReviewStatus.Published }
        && context.New is { EffectiveStatus: ReviewStatus.Published };

    public Task<PublishInstruction> BuildAsync(
        StreamContext<ReviewStreamImage> context, CancellationToken cancellationToken = default)
    {
        var oldReview = context.Old!;
        var newReview = context.New!;

        return Task.FromResult(new PublishInstruction(
            nameof(ReviewUpdatedEvent),
            new ReviewUpdatedEvent
            {
                ReviewId = newReview.Id,
                ProductId = newReview.ProductId,
                OldRating = oldReview.Rating,
                NewRating = newReview.Rating
            }));
    }
}
